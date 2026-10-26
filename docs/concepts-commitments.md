---
title: Commitments and temporal values
parent: Concepts
nav_order: 1
nav_exclude: false
permalink: /concepts/commitments/
---

Canonical `Sla` and `Lifecycle` commitments are optional. Missing commitments remain absent; normalization does not invent guarantees or compare their strength.

ODCS freshness, latency, frequency and retention commitments keep separate entries for their
dataset or field scopes. Each entry retains its typed duration and description.

Elapsed durations compare by ticks, so 120 minutes, 2 hours and 7200 seconds are equivalent.
Calendar years normalize to months. Calendar months remain distinct from elapsed days because
their duration depends on the reference date.

Availability commitments retain percentage targets and recurring time windows, including
optional end times and dataset or field scopes. Times with offsets normalize to UTC;
times without offsets keep an unspecified zone.

Lifecycle dates describe `generalAvailability`, `endOfSupport` and `endOfLife` as UTC
instants. A date without a time means midnight UTC. A timestamp must include an explicit offset.

When an SLA value cannot be normalized, Packata keeps it under
`Extensions["odcs"]["slaProperties"]` and reports a diagnostic. This includes unsupported
values, malformed or ambiguous scopes, unknown properties, extended duration bounds and
duplicate milestones. These values are not silently overwritten.

Normalizing commitments does not schedule monitoring or execute a policy.

## Reading temporal data

Reading temporal data is separate from normalizing commitments. Readers apply declared
strptime patterns to indexed and named schemas, preserving date and time formats.

An offset-bearing value becomes `DateTimeOffset`. An offset-free datetime remains `DateTime`,
and a value missing a required offset fails. Packata does not infer a timezone for a displayed
local time.