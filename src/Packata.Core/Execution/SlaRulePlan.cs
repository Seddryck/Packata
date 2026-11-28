using System.Numerics;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

internal sealed class SlaRulePlan(RuleDescriptor descriptor, SlaEvaluationBinding binding,
    ContractDuration? duration, decimal? percentage, AvailabilityWindow? recurrence) : IExecutableRule
{
    public RuleDescriptor Descriptor { get; } = descriptor;
    public InputRequirements Requirements { get; } = new(QualityInputScope.Observation, RequiresCompleteInput: false);

    public async ValueTask<RuleResult> ExecuteAsync(RuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SlaObservationWindow? window = null;
        TimeSpan? limit = null;
        string? temporalIssue = null;
        try
        {
            window = SlaObservationTime.Window(context.ObservationTime, binding, recurrence);
            if (duration is { } commitment)
                limit = context.ObservationTime - SlaObservationTime.Subtract(context.ObservationTime, commitment, binding.CalendarTimeZone);
        }
        catch (IncompleteRuleInputException exception)
        {
            temporalIssue = exception.Code;
        }
        catch (ArgumentException)
        {
            temporalIssue = "SLA_TIME_OUT_OF_RANGE";
        }

        var op = binding.Policy == SlaMeasurementPolicy.ContinuousRetainedCoverage || percentage.HasValue
            ? ExpectationOperator.GreaterOrEqual : ExpectationOperator.LessOrEqual;
        var declared = new QualityExpectation(op, duration is { } d ? QualityValue.From(d) : QualityValue.From(percentage));
        var monitoringTicks = window is { } observed ? (observed.End - observed.Start).Ticks : 0;
        var effective = percentage is { } target
            ? QualityValue.From(RequiredAvailableTicks(monitoringTicks, target))
            : QualityValue.From(limit);
        var plan = new MeasuredExecutableRule(Descriptor, Requirements,
            new SlaMeasurementPlan(binding, window, limit, temporalIssue), new ExpectationAssertionPlan([new(op, effective)]));
        var result = await plan.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        var measurement = result.Measurement with
        {
            SlaWindow = window,
            SlaSnapshotId = context.SlaObservations?.SnapshotId,
            Unit = percentage.HasValue ? "percent" : "elapsed duration"
        };
        if (percentage.HasValue && measurement.Value?.ToObject() is long availableTicks)
            measurement = measurement with { Value = QualityValue.From(100m * availableTicks / monitoringTicks) };
        return result with { Measurement = measurement, Expectations = Array.AsReadOnly(new[] { declared }) };
    }

    // Compare integer monitoring ticks against the exact declared decimal, even when reporting rounds a percentage.
    private static long RequiredAvailableTicks(long totalTicks, decimal target)
    {
        var parts = decimal.GetBits(target);
        var coefficient = (new BigInteger((uint)parts[2]) << 64)
            + (new BigInteger((uint)parts[1]) << 32) + (uint)parts[0];
        var denominator = BigInteger.Pow(10, ((parts[3] >> 16) & 0xff) + 2);
        var whole = BigInteger.DivRem(coefficient * totalTicks, denominator, out var remainder);
        return (long)(whole + (remainder.IsZero ? 0 : 1));
    }
}

internal static class SlaObservationTime
{
    public static DateTimeOffset Subtract(DateTimeOffset reference, ContractDuration duration, TimeZoneInfo? zone)
    {
        if (duration.CalendarMonths == 0)
            return reference.ToUniversalTime().Subtract(duration.Elapsed);
        var local = TimeZoneInfo.ConvertTime(reference, zone!).DateTime.AddMonths(-duration.CalendarMonths);
        return Resolve(local, zone!).ToUniversalTime().Subtract(duration.Elapsed);
    }

    public static SlaObservationWindow Window(DateTimeOffset reference, SlaEvaluationBinding binding, AvailabilityWindow? daily)
    {
        if (daily is null)
            return new(Subtract(reference, binding.Lookback!.Value, binding.CalendarTimeZone), reference);

        var zone = binding.CalendarTimeZone;
        var offset = daily.UtcOffset;
        var local = offset is { } declaredOffset ? reference.ToOffset(declaredOffset) : TimeZoneInfo.ConvertTime(reference, zone!);
        var end = daily.End!.Value;
        var endDate = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) < end)
            endDate = endDate.AddDays(-1);
        var startDate = daily.Start > end ? endDate.AddDays(-1) : endDate;
        var startInstant = At(startDate, daily.Start);
        var endInstant = At(endDate, end);
        if (startInstant >= endInstant || endInstant > reference)
            throw new IncompleteRuleInputException("SLA_UNRESOLVED_WINDOW");
        return new(startInstant, endInstant);

        DateTimeOffset At(DateOnly day, TimeOnly time)
        {
            var wallTime = day.ToDateTime(time);
            return offset is { } fixedOffset ? new DateTimeOffset(wallTime, fixedOffset) : Resolve(wallTime, zone!);
        }
    }

    public static DateTimeOffset Resolve(DateTime wallTime, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(wallTime) || zone.IsAmbiguousTime(wallTime))
            throw new IncompleteRuleInputException("SLA_AMBIGUOUS_LOCAL_TIME");
        return new(wallTime, zone.GetUtcOffset(wallTime));
    }
}
