---
title: Commitments and temporal values
parent: Concepts
nav_order: 1
nav_exclude: false
permalink: /concepts/commitments/
---

Canonical `Sla` and `Lifecycle` commitments are optional. Missing commitments remain absent; normalization does not invent guarantees or compare their strength.

ODCS freshness, latency, frequency and retention preserve separate scoped entries with typed durations, dataset/field references and descriptions. Elapsed durations compare by ticks: 120 minutes, 2 hours and 7200 seconds are equivalent. Calendar years normalize to months; calendar months are distinct from elapsed days.

Availability preserves percentage targets and multiple recurring windows with optional end times and dataset/field scopes. Offset-bearing times normalize to UTC; values without offsets retain an unspecified zone. Lifecycle `generalAvailability`, `endOfSupport` and `endOfLife` map to UTC instants. Date-only lifecycle values mean midnight UTC; timestamps require an explicit offset.

Unsupported values, malformed or ambiguous scopes, unknown properties, extended duration bounds and duplicate milestones are retained under `Extensions["odcs"]["slaProperties"]` with diagnostics rather than silently overwritten. Canonical normalization does not schedule monitoring or execute policies.

## Reading temporal data

Temporal data parsing is a separate reader concern. Declared strptime patterns apply to indexed and named schemas. A declared offset is retained as `DateTimeOffset`; offset-free datetime values remain `DateTime`, and a missing required offset fails. Date and time formats are preserved. Do not assume that a displayed local time has a timezone inferred by Packata.
