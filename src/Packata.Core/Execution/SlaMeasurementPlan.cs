using System.Runtime.CompilerServices;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

internal sealed class SlaMeasurementPlan(SlaEvaluationBinding binding, SlaObservationWindow? window,
    TimeSpan? durationLimit, string? temporalIssue) : IMeasurementPlan
{
    public async ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
    {
        if (temporalIssue is not null || window is not { } requested || requested.Start >= requested.End)
            return Incomplete(temporalIssue ?? "SLA_UNRESOLVED_WINDOW", "The measurement window or calendar threshold could not be resolved.");
        if (binding.Policy == SlaMeasurementPolicy.ContinuousRetainedCoverage && requested.End - requested.Start < durationLimit)
            return Incomplete("SLA_RETENTION_WINDOW_TOO_SHORT", "The observation window cannot establish the required retention.");
        if (context.SlaObservations is not { } run || string.IsNullOrWhiteSpace(run.SnapshotId))
            return Incomplete("SLA_OBSERVATIONS_MISSING", "The run must supply an observation source and snapshot identity.");

        var request = new SlaObservationRequest(binding, run.SnapshotId, context.ObservationTime, requested);
        var snapshot = await run.Source.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return Incomplete("SLA_OBSERVATIONS_MISSING", "The requested observations are unavailable.");
        if (snapshot.SnapshotId != run.SnapshotId || snapshot.AsOf != context.ObservationTime)
            return Incomplete("SLA_SNAPSHOT_MISMATCH", "The observations do not match the run snapshot and reference time.");
        if (!snapshot.Complete || snapshot.Coverage.Start > requested.Start || snapshot.Coverage.End < requested.End
            || snapshot.Coverage.End > snapshot.AsOf)
            return Incomplete("SLA_PARTIAL_WINDOW", "The observation source does not certify coverage of the entire required window.");

        var population = 0L;
        var rows = CountedRows(cancellationToken);
        var measurement = binding.Policy switch
        {
            SlaMeasurementPolicy.AgeOfLatestTimestamp => await FreshnessAsync(rows, requested).ConfigureAwait(false),
            SlaMeasurementPolicy.MaximumPairedDeliveryLatency => await LatencyAsync(rows, requested).ConfigureAwait(false),
            SlaMeasurementPolicy.MaximumTimeSinceUpdate => await FrequencyAsync(rows, requested).ConfigureAwait(false),
            _ => await IntervalsAsync(rows, requested).ConfigureAwait(false)
        };
        return measurement with { Population = population };

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CountedRows([EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var row in snapshot.Rows.WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (population == context.Limits.MaxRows)
                    throw new IncompleteRuleInputException("ROW_BUDGET");
                population++;
                yield return row;
            }
        }
    }

    private async ValueTask<RuleMeasurement> FreshnessAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        DateTimeOffset? latest = null;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var timestamp = Timestamp(row, binding.TimestampField);
            RequireInWindow(timestamp, requested);
            if (latest is null || timestamp > latest)
                latest = timestamp;
        }

        return latest is { } maximum
            ? new(QualityValue.From(requested.End - maximum), Evidence: [Evidence("latestTimestamp", maximum)])
            : Empty();
    }

    private async ValueTask<RuleMeasurement> LatencyAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        (DateTimeOffset Source, DateTimeOffset Delivery)? worst = null;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var source = Timestamp(row, binding.TimestampField);
            var delivery = Timestamp(row, binding.EndTimestampField!);
            RequireInWindow(delivery, requested);
            if (source > delivery)
                return Incomplete("SLA_INVALID_PAIR", "A delivery precedes its paired source event.");
            if (worst is not { } pair || delivery - source > pair.Delivery - pair.Source)
                worst = (source, delivery);
        }

        return worst is { } maximum
            ? new(QualityValue.From(maximum.Delivery - maximum.Source),
                Evidence: [Evidence("sourceTimestamp", maximum.Source), Evidence("deliveryTimestamp", maximum.Delivery)])
            : Empty();
    }

    private async ValueTask<RuleMeasurement> FrequencyAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        DateTimeOffset? previous = null;
        var maximum = TimeSpan.Zero;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var timestamp = Timestamp(row, binding.TimestampField);
            if (timestamp > requested.End)
                throw new IncompleteRuleInputException("SLA_OUT_OF_WINDOW_TIMESTAMP");
            if (previous is { } earlier)
            {
                if (timestamp <= requested.Start || timestamp < earlier)
                    return Incomplete("SLA_INVALID_UPDATE_HISTORY", "Updates after the initial anchor must be ordered within the window.");
                maximum = Max(maximum, timestamp - earlier);
            }
            else if (timestamp > requested.Start)
            {
                return Incomplete("SLA_MISSING_UPDATE_ANCHOR", "Frequency requires the last update at or before the window start.");
            }
            previous = timestamp;
        }

        return previous is { } last
            ? new(QualityValue.From(Max(maximum, requested.End - last)), Evidence: [Evidence("lastUpdate", last)])
            : Empty();
    }

    private async ValueTask<RuleMeasurement> IntervalsAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        var retention = binding.Policy == SlaMeasurementPolicy.ContinuousRetainedCoverage;
        var seen = false;
        var endOfCoverage = requested.Start;
        var continuousStart = requested.Start;
        var availableTicks = 0L;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var start = Timestamp(row, binding.TimestampField);
            var end = Timestamp(row, binding.EndTimestampField!);
            if (start < endOfCoverage || end <= start || end > requested.End)
                return Incomplete("SLA_INVALID_INTERVAL", "Intervals must be ordered, non-overlapping and clipped to the measurement window.");
            if (start > endOfCoverage)
            {
                if (!retention)
                    return Incomplete("SLA_MONITORING_GAP", "Availability requires explicit monitoring status throughout the window.");
                continuousStart = start;
            }

            if (!retention)
            {
                if (!row.TryGetValue(binding.AvailabilityField!, out var status) || status is not bool up)
                    return Incomplete("SLA_MISSING_STATUS", "Every monitoring interval needs an explicit boolean availability status.");
                if (up)
                    availableTicks += (end - start).Ticks;
            }
            endOfCoverage = end;
            seen = true;
        }

        if (!seen)
            return Empty();
        if (retention)
        {
            var coverage = endOfCoverage == requested.End ? requested.End - continuousStart : TimeSpan.Zero;
            return new(QualityValue.From(coverage), Evidence: [Evidence("lastRetainedEnd", endOfCoverage)]);
        }
        if (endOfCoverage != requested.End)
            return Incomplete("SLA_MONITORING_GAP", "Availability requires explicit monitoring status throughout the window.");
        return new(QualityValue.From(availableTicks), Evidence:
            [Evidence("availableTicks", availableTicks), Evidence("monitoredTicks", (requested.End - requested.Start).Ticks)]);
    }

    private DateTimeOffset Timestamp(IReadOnlyDictionary<string, object?> row, string field)
    {
        row.TryGetValue(field, out var value);
        try
        {
            return value switch
            {
                DateTimeOffset offset => offset,
                DateTime { Kind: DateTimeKind.Utc } utc => new DateTimeOffset(utc),
                DateTime { Kind: DateTimeKind.Unspecified } wallTime when binding.SourceTimeZone is { } zone
                    => SlaObservationTime.Resolve(wallTime, zone),
                DateTime => throw new IncompleteRuleInputException("SLA_UNSPECIFIED_TIMESTAMP_ZONE"),
                _ => throw new IncompleteRuleInputException("SLA_TIMESTAMP_MISSING_OR_INVALID")
            };
        }
        catch (ArgumentException)
        {
            throw new IncompleteRuleInputException("SLA_TIME_OUT_OF_RANGE");
        }
    }

    private static void RequireInWindow(DateTimeOffset timestamp, SlaObservationWindow requested)
    {
        if (timestamp < requested.Start || timestamp > requested.End)
            throw new IncompleteRuleInputException("SLA_OUT_OF_WINDOW_TIMESTAMP");
    }

    private static RuleEvidence Evidence(string name, object value) => new(name, QualityValue.From(value));

    private static RuleMeasurement Empty() => Incomplete("SLA_EMPTY_OBSERVATIONS", "The measurement has no observation rows.");

    private static RuleMeasurement Incomplete(string code, string message)
        => new(null, Complete: false, Diagnostics: [new(code, message)]);

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;
}
