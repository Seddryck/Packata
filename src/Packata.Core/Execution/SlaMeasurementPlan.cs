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
        var finding = ValidateSnapshot(snapshot, request);
        return finding ?? await MeasureSnapshotAsync(snapshot!.Rows, requested, context.Limits.MaxRows, cancellationToken).ConfigureAwait(false);
    }

    private static RuleMeasurement? ValidateSnapshot(SlaObservationSnapshot? snapshot, SlaObservationRequest request)
    {
        if (snapshot is null)
            return Incomplete("SLA_OBSERVATIONS_MISSING", "The requested observations are unavailable.");
        if (snapshot.SnapshotId != request.SnapshotId || snapshot.AsOf != request.ObservationTime)
            return Incomplete("SLA_SNAPSHOT_MISMATCH", "The observations do not match the run snapshot and reference time.");
        if (!snapshot.Complete || snapshot.Coverage.Start > request.Window.Start || snapshot.Coverage.End < request.Window.End
            || snapshot.Coverage.End > snapshot.AsOf)
            return Incomplete("SLA_PARTIAL_WINDOW", "The observation source does not certify coverage of the entire required window.");
        return null;
    }

    private async ValueTask<RuleMeasurement> MeasureSnapshotAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> source,
        SlaObservationWindow requested, long rowLimit, CancellationToken cancellationToken)
    {
        var population = 0L;
        var rows = CountedRows(cancellationToken);
        var measurement = await MeasureRowsAsync(rows, requested).ConfigureAwait(false);
        return measurement with { Population = population };

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CountedRows([EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var row in source.WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (population == rowLimit)
                    throw new IncompleteRuleInputException("ROW_BUDGET");
                population++;
                yield return row;
            }
        }
    }

    private ValueTask<RuleMeasurement> MeasureRowsAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
        => binding.Policy switch
        {
            SlaMeasurementPolicy.AgeOfLatestTimestamp => FreshnessAsync(rows, requested),
            SlaMeasurementPolicy.MaximumPairedDeliveryLatency => LatencyAsync(rows, requested),
            SlaMeasurementPolicy.MaximumTimeSinceUpdate => FrequencyAsync(rows, requested),
            SlaMeasurementPolicy.ContinuousRetainedCoverage => RetentionAsync(rows, requested),
            _ => AvailabilityAsync(rows, requested)
        };

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

    private async ValueTask<RuleMeasurement> RetentionAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        var endOfCoverage = requested.Start;
        var continuousStart = requested.Start;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var (start, end) = ReadInterval(row);
            if (!ValidInterval(start, end, endOfCoverage, requested.End))
                return InvalidInterval();
            if (start > endOfCoverage)
                continuousStart = start;
            endOfCoverage = end;
        }

        if (endOfCoverage == requested.Start)
            return Empty();
        var coverage = endOfCoverage == requested.End ? requested.End - continuousStart : TimeSpan.Zero;
        return new(QualityValue.From(coverage), Evidence: [Evidence("lastRetainedEnd", endOfCoverage)]);
    }

    private async ValueTask<RuleMeasurement> AvailabilityAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, SlaObservationWindow requested)
    {
        var monitoredThrough = requested.Start;
        var availableTicks = 0L;
        await foreach (var row in rows.ConfigureAwait(false))
        {
            var (start, end) = ReadInterval(row);
            if (!ValidInterval(start, end, monitoredThrough, requested.End))
                return InvalidInterval();
            if (start != monitoredThrough)
                return MonitoringGap();
            if (!row.TryGetValue(binding.AvailabilityField!, out var status) || status is not bool up)
                return Incomplete("SLA_MISSING_STATUS", "Every monitoring interval needs an explicit boolean availability status.");
            if (up)
                availableTicks += (end - start).Ticks;
            monitoredThrough = end;
        }

        if (monitoredThrough == requested.Start)
            return Empty();
        if (monitoredThrough != requested.End)
            return MonitoringGap();
        return new(QualityValue.From(availableTicks), Evidence:
            [Evidence("availableTicks", availableTicks), Evidence("monitoredTicks", (requested.End - requested.Start).Ticks)]);
    }

    private (DateTimeOffset Start, DateTimeOffset End) ReadInterval(IReadOnlyDictionary<string, object?> row)
        => (Timestamp(row, binding.TimestampField), Timestamp(row, binding.EndTimestampField!));

    private static bool ValidInterval(DateTimeOffset start, DateTimeOffset end, DateTimeOffset previousEnd, DateTimeOffset windowEnd)
        => start >= previousEnd && end > start && end <= windowEnd;

    private static RuleMeasurement InvalidInterval()
        => Incomplete("SLA_INVALID_INTERVAL", "Intervals must be ordered, non-overlapping and clipped to the measurement window.");

    private static RuleMeasurement MonitoringGap()
        => Incomplete("SLA_MONITORING_GAP", "Availability requires explicit monitoring status throughout the window.");

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
