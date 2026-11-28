---
title: Service commitments and dates
parent: Concepts
nav_order: 1
nav_exclude: false
permalink: /concepts/commitments/
---

A contract can describe more than the shape of a dataset. It can also promise
when data will arrive, how long it will be kept, or when support will end. Packata
keeps these promises alongside the dataset's schema so applications can inspect
them without having to interpret each contract standard separately.

For example, a customer dataset might promise an update every two hours and
retention for one calendar month. Those are declarations in the contract. Loading
them into Packata does not start a refresh job, delete old records, or monitor
whether the promises are being met.

## Describe a service commitment

Service-level commitments describe expectations about data delivery or access.
In ODCS, these can include freshness, latency, update frequency, retention and
availability. A commitment can apply to a whole dataset or to a particular field.
Packata preserves that scope, its duration or target, and its description.

For example, a promise about the age of `customers` data is different from one
about a particular timestamp field. Packata keeps those as separate entries
rather than combining them into one dataset-wide promise.

In the shared .NET model, these declarations are held in `Sla` and `Lifecycle`.
Both are optional. If the source contract makes no commitment, Packata leaves it
absent rather than inventing a default guarantee.

## Understand durations

The same elapsed time can be written in several ways. A duration of 120 minutes,
2 hours or 7200 seconds represents the same interval. Packata converts these to a
common representation so equivalent durations can be recognized.

Calendar durations need different treatment. One year can be represented as
12 months, but a month cannot be replaced by a fixed number of days: February
and March have different lengths. Packata therefore keeps calendar months
separate from elapsed days.

This conversion makes the values consistent to inspect. It does not decide
whether one service commitment is better or stronger than another.

## Understand availability windows

An availability commitment can contain a percentage target and a recurring time
window. Packata retains the target, the window's start, its end when supplied,
and the dataset or field it applies to.

A time with an explicit UTC offset can be converted to UTC. For example,
`09:00+02:00` becomes `07:00` UTC. A time written simply as `09:00` has no known
timezone; Packata leaves that timezone unspecified instead of assuming one.

## Evaluate observed commitments

`Packata.Core.Execution.SlaRuleCompiler` compiles a normalized `Sla` with
deployment-specific `SlaEvaluationBindings`. Each binding selects a commitment
by its kind and zero-based position, then names the contractual target, endpoint,
observation source, policy and observation fields. Use the positions from the
same normalized declaration being compiled. Supply `RuleCompilationContext`
with the contract to validate asset and endpoint identities against it.

This example checks whether the newest `orders.loaded_at` timestamp is at most
two elapsed hours old. The observation source supplies the bound column for a
complete 24-hour lookback as of the fixed reference time.

```csharp
using Packata.Core.Contracts;
using Packata.Core.Execution;

var sla = new Sla(Freshness:
[
    new(new ContractDuration(0, TimeSpan.FromHours(2)), [new("orders", "loaded_at")])
]);
var binding = new SlaEvaluationBinding(
    new(SlaCommitmentKind.Freshness, 0), new("orders", ["loaded_at"]),
    "production", "orders-ingestion", SlaMeasurementPolicy.AgeOfLatestTimestamp,
    TimestampField: "loaded_at", Lookback: new(0, TimeSpan.FromHours(24)));
var compilation = new SlaRuleCompiler(new("orders-sla", [binding]))
    .Compile(sla, new(EndpointId: "production"));
var reference = new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);
var context = new RuleExecutionContext(reference)
{
    SlaObservations = new("orders-snapshot-20260331T1200Z", observationSource)
};
var report = await RuleRunner.ExecuteAsync([compilation], context);
```

`observationSource` is the deployment's `ISlaObservationSource`. It resolves the
exact source, target, endpoint, window and snapshot in each request. Return
`null` when the requested observation is unavailable. A complete snapshot
certifies the policy-specific population and coverage of the entire window as of
the run's reference time. Give each request an independent enumeration of its
snapshot rows, pass cancellation through to the reader, and release resources
when the enumerator is disposed. If a read ends prematurely after opening, throw
`IncompleteRuleInputException`. The executor applies row, time and evidence
limits and uses constant measurement state for each policy.

| Policy | Required observations and comparison |
| --- | --- |
| `AgeOfLatestTimestamp` | Return all observations for the bound timestamp in the lookback, including both boundaries. Compare the reference time minus the maximum timestamp with the inclusive freshness limit. Choose the lookback to cover the age the deployment needs to measure. |
| `MaximumPairedDeliveryLatency` | Return one row per delivery in the lookback, including both boundaries. Each row must contain the timestamp of the delivery and its corresponding source event. The source event can precede the window. Compare the largest delivery-minus-source duration with the inclusive latency limit. |
| `MaximumTimeSinceUpdate` | Return the latest update at or before the window start as the first row, followed by every update after the start through the end in timestamp order. Compare the largest elapsed time since a preceding update, including the gap from the last update to the reference time, with the inclusive frequency limit. |
| `ContinuousRetainedCoverage` | Return an authoritative inventory of retained intervals, ordered, non-overlapping and clipped to the lookback. Omitted intervals in a complete inventory indicate gaps in retention. Compare the contiguous retained interval ending at the reference time with the inclusive minimum. The lookback must cover the minimum under evaluation. |
| `TimeWeightedAvailability` | Return ordered, contiguous, non-overlapping monitoring intervals covering the exact lookback. Each interval needs a boolean availability status. Divide available elapsed ticks by all monitored elapsed ticks and compare the exact ratio with the inclusive percentage target. |
| `ContinuousAvailabilityInDailyWindow` | Return the same complete interval monitoring for the most recent fully elapsed daily occurrence. An end earlier than the start denotes an overnight occurrence. The occurrence passes at 100 percent availability. Use the declaration's offset when present. Otherwise bind an explicit calendar time zone. |

Consecutive intervals share a boundary and are interpreted as start-inclusive,
end-exclusive. Timestamp values can be `DateTimeOffset`, UTC `DateTime`, or
offsetless `DateTime` with an explicit `SourceTimeZone`. Ambiguous or nonexistent
local times are inconclusive. Null, missing, future or out-of-window timestamps,
empty observations, stale snapshots and partial reads cannot establish compliance.

Lookbacks and duration thresholds with months require a `CalendarTimeZone`. For
those values, convert the fixed reference instant to local wall time, subtract
whole months with end-of-month clamping, resolve that local time, then subtract
any elapsed component on the UTC timeline. The distance between the resulting
instant and the reference supplies the elapsed comparison threshold. Pure
elapsed durations use timeline ticks directly.

The report retains SLA identity and binding, the declared expectation, the
reference time, the snapshot and measurement window, and the measured duration
or percentage with bounded evidence. Unsupported commitments such as start-only
availability windows or commitments scoped to multiple elements remain
compilation findings, as do missing or ambiguous bindings. Missing or incomplete
observations produce an inconclusive execution result. This API evaluates
operational observations. Contract compatibility and lifecycle milestones have
their own models.

## Understand lifecycle dates

Lifecycle milestones describe when a dataset becomes generally available, loses
support, or reaches the end of its life. Their model names are
`generalAvailability`, `endOfSupport` and `endOfLife`.

Packata represents these milestones as UTC instants. A date without a time,
such as `2025-06-01`, means midnight UTC on that date. If a timestamp includes a
time, it must also include an explicit offset so the instant is unambiguous.
For example, `2025-06-01T10:00:00+02:00` represents `08:00` UTC.

## Check values Packata could not interpret

Some declarations cannot be converted reliably into the shared model. A scope
may be ambiguous, a property may be unknown, or a duration may use unsupported
bounds. Duplicate milestones can also prevent a clear interpretation.

For ODCS SLA declarations, Packata reports a diagnostic and retains the original
information under `Extensions["odcs"]["slaProperties"]`. Unsupported or malformed
values are not silently overwritten. Review these diagnostics before treating the
mapped model as a complete account of the contract's promises.

## Reading temporal data

Dates and times inside dataset records are a separate concern. A service promise
about freshness does not tell a reader how to parse a timestamp column. Readers
use the field's declared date/time format, including supported strptime patterns,
for both indexed and named schemas.

A value whose format includes a UTC offset is read as `DateTimeOffset`. A datetime
without an offset is read as `DateTime`. If the declared format requires an offset
but the value has none, reading fails rather than guessing a timezone.

For example, a local event time recorded as `2025-06-01 09:00` does not establish
whether the event happened at 09:00 in Paris, London or another timezone. Packata
preserves that distinction instead of treating the displayed time as UTC.

The [reader guide]({{ '/guides/readers-storage/' | relative_url }}) explains how
to configure the readers that apply those formats. For the separate question of
whether a contract change could break consumers, see
[contract compatibility]({{ '/compatibility/' | relative_url }}).
