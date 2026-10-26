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
