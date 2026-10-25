namespace Packata.Core.Contracts;

/// <summary>Operational commitments. Empty lists mean that no commitment was provided.</summary>
public sealed record Sla(
    IReadOnlyList<DurationCommitment>? Freshness = null,
    IReadOnlyList<DurationCommitment>? Latency = null,
    IReadOnlyList<DurationCommitment>? Frequency = null,
    IReadOnlyList<DurationCommitment>? Retention = null,
    Availability? Availability = null)
{
    public IReadOnlyList<DurationCommitment> Freshness { get; init; } = Freshness ?? [];
    public IReadOnlyList<DurationCommitment> Latency { get; init; } = Latency ?? [];
    public IReadOnlyList<DurationCommitment> Frequency { get; init; } = Frequency ?? [];
    public IReadOnlyList<DurationCommitment> Retention { get; init; } = Retention ?? [];
}

/// <summary>
/// A duration with calendar months separate from elapsed time. Years normalize to months;
/// weeks, days, hours, minutes and seconds normalize to ticks. Calendar months cannot be
/// compared to elapsed days without a reference date.
/// </summary>
public readonly record struct ContractDuration(int CalendarMonths, TimeSpan Elapsed);

public sealed record ElementReference(string Dataset, string? Field = null);

public sealed record DurationCommitment(
    ContractDuration Duration,
    IReadOnlyList<ElementReference> Elements,
    string? Description = null);

public sealed record Availability(
    IReadOnlyList<AvailabilityTarget>? Target = null,
    IReadOnlyList<AvailabilityWindow>? Windows = null)
{
    public IReadOnlyList<AvailabilityTarget> Target { get; init; } = Target ?? [];
    public IReadOnlyList<AvailabilityWindow> Windows { get; init; } = Windows ?? [];
}

/// <summary>Availability expressed as a percentage in the inclusive range 0 to 100.</summary>
public sealed record AvailabilityTarget(
    decimal Percentage,
    IReadOnlyList<ElementReference> Elements,
    string? Description = null);

/// <summary>
/// A recurring availability time, optionally ending at another time. Offset-bearing times
/// are normalized to UTC; a null offset preserves an unspecified time zone.
/// </summary>
public sealed record AvailabilityWindow(
    TimeOnly Start,
    TimeOnly? End,
    TimeSpan? UtcOffset,
    IReadOnlyList<ElementReference> Elements,
    string? Description = null);

public sealed record Lifecycle(
    DateTimeOffset? GeneralAvailability = null,
    DateTimeOffset? EndOfSupport = null,
    DateTimeOffset? EndOfLife = null);
