using Packata.Core.Contracts;

namespace Packata.Core.Execution;

public enum SlaCommitmentKind
{
    Freshness,
    Latency,
    Frequency,
    Retention,
    AvailabilityTarget,
    AvailabilityWindow
}

/// <summary>Identifies a commitment by its position within one normalized SLA list.</summary>
public readonly record struct SlaCommitmentReference(SlaCommitmentKind Kind, int Index);

public enum SlaMeasurementPolicy
{
    Unspecified,
    AgeOfLatestTimestamp,
    MaximumPairedDeliveryLatency,
    MaximumTimeSinceUpdate,
    ContinuousRetainedCoverage,
    TimeWeightedAvailability,
    ContinuousAvailabilityInDailyWindow
}

/// <summary>The timestamp fields are observation column names. The target identifies the contractual subject.</summary>
public sealed record SlaEvaluationBinding(SlaCommitmentReference Commitment, QualityTarget Target,
    string EndpointId, string SourceId, SlaMeasurementPolicy Policy,
    string TimestampField, ContractDuration? Lookback = null, string? EndTimestampField = null,
    string? AvailabilityField = null, TimeZoneInfo? SourceTimeZone = null, TimeZoneInfo? CalendarTimeZone = null);

public sealed class SlaEvaluationBindings
{
    public SlaEvaluationBindings(string slaId, IEnumerable<SlaEvaluationBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slaId);
        SlaId = slaId;
        Bindings = Array.AsReadOnly(bindings.ToArray());
    }

    public string SlaId { get; }
    public IReadOnlyList<SlaEvaluationBinding> Bindings { get; }
}

public readonly record struct SlaObservationWindow(DateTimeOffset Start, DateTimeOffset End);

public sealed record SlaObservationRequest(SlaEvaluationBinding Binding, string SnapshotId,
    DateTimeOffset ObservationTime, SlaObservationWindow Window);

/// <summary>
/// An authoritative observation for the requested target and policy. Complete certifies the entire
/// coverage window and policy-specific population. A partial enumeration must fail with
/// IncompleteRuleInputException. The enumerator owns and releases any open resources.
/// </summary>
public sealed record SlaObservationSnapshot(string SnapshotId, DateTimeOffset AsOf,
    SlaObservationWindow Coverage, bool Complete, IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows);

public interface ISlaObservationSource
{
    /// <summary>Resolve the exact request or return null when it is unavailable.</summary>
    ValueTask<SlaObservationSnapshot?> ObserveAsync(SlaObservationRequest request, CancellationToken cancellationToken);
}

public sealed record SlaObservationRun(string SnapshotId, ISlaObservationSource Source);
