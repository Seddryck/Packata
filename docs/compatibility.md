---
title: Contract compatibility
nav_order: 3
nav_exclude: false
permalink: /compatibility/
parent: Concepts
---

# Contract compatibility

`packata contract compare <BASELINE> <CANDIDATE>` answers: **can consumers built
against the baseline safely consume data produced under the candidate?**
Direction is always baseline (previous) to candidate (current). Swap arguments
to ask the reverse question. This assesses declared guarantees, not actual data
or whether candidate producers accept every previously valid input.

## Policy: baseline-consumers-v1

Consumers access datasets/fields by logical name and tolerate additional
datasets/fields (open records). Stronger producer guarantees are safe; weaker
guarantees can break baseline consumers. No dataset/database connection is opened.

| Change | Classification |
| --- | --- |
| Dataset/field added | nonBreaking under the open-record assumption |
| Dataset/field removed | breaking |
| Required → optional | breaking: presence is no longer guaranteed |
| Optional → required | nonBreaking |
| Non-nullable → nullable | breaking |
| Nullable → non-nullable | nonBreaking |
| integer → number | breaking: fractional values become possible |
| number → integer | nonBreaking: all candidate values fit the baseline type |
| Other distinct supported logical types | breaking |
| datetime ↔ timestamp in canonical inputs | nonBreaking: logical aliases |
| Minimum increased, maximum decreased, enum narrowed | nonBreaking |
| Minimum decreased, maximum increased, enum expanded/removed | breaking |
| Numeric inclusive/exclusive bounds | compare effective bounds, including strictness |
| min/maxLength, min/maxItems, min/maxProperties | tightening is nonBreaking; loosening/removing is breaking |
| unique, uniqueItems, primaryKey field guarantee | enabling is nonBreaking; removing/disabling is breaking |
| Pattern or object-required constraint changed | unknown: general implication is not implemented |
| Physical name/type/format, dataset binding, server changed | potentiallyBreaking: impact depends on access/representation assumptions |
| Description or ordinary contract metadata changed | nonBreaking |
| Contract ID/name changed | potentiallyBreaking |
| Relationship, quality, SLA, governance, extension semantics changed | unknown |
| Unsupported type/constraint, unmapped source member, mapping warning, missing schema | explicit unknown finding, even for identical inputs |

The policy supports strings, integers, numbers, booleans, dates, times, timestamps and datetimes,
objects and arrays. It does not infer implications for decimal precision or scale, temporal
bounds, maps, vectors, arbitrary patterns, JSON Schema or custom semantics.

Numeric bounds must be finite decimal values; unsupported values produce unknown findings.
Interactions between constraints are assessed conservatively. A weaker bound can therefore
be breaking even when another constraint would restrict the same values.

For ODCS, omitted `required` and `nullable` values both default to false. They describe
independent guarantees: whether a field must be present and whether its value may be null.

Data Package requiredness also rejects null or missing values. For that standard, comparison
derives nullability from requiredness instead of using the canonical mapper's default of false.

Matching follows `ContractDiffer` and treats dataset IDs and field names as case-sensitive.
Changing an identifier produces a removal and an addition, even when it could be a rename;
the policy does not infer renames. Adding or removing an array item definition is unknown.

A single `PropertyModified` change can affect several guarantees and produce several findings.
Each finding keeps the original typed change along with its own path, rule and explanation.

Core checks the original descriptor against a conservative list of covered members.
Unrecognized members, including nested ones ignored by native deserializers, produce `CMP020`
findings. Their presence in both inputs does not establish compatibility.

Some native extension objects cannot be snapshotted by the factual differ. These cause failed
analysis rather than an empty compatible result. Changes to normalized SLA or lifecycle values
that are absent from the factual diff are explicitly reported as unknown.

The highest-severity finding determines the outcome: breaking, then
potentiallyBreaking, then unknown, otherwise compatible. All findings remain
available. Completed analysis with unknown findings differs from failed or
incomplete analysis; neither establishes compatibility.

## Reusable API

```csharp
CompatibilityResult result = ContractCompatibility.Compare(previous, current);
foreach (var finding in result.Findings)
    Console.WriteLine($"{finding.Classification} {finding.Path}: {finding.Explanation}");
```

This overload compares canonical models under canonical assumptions. Native
integrations carry their source provenance and mapping diagnostics:

```csharp
var baseline = new CompatibilityInput(previousMapping.Value, ContractStandard.Odcs,
    previousMapping.Diagnostics, previousDescriptor);
var candidate = new CompatibilityInput(currentMapping.Value, ContractStandard.Odcs,
    currentMapping.Diagnostics, currentDescriptor);
CompatibilityResult result = ContractCompatibility.Compare(baseline, candidate, cancellationToken);
```

Supply a descriptor as a tree of `IReadOnlyDictionary<string, object?>` values, object lists
and scalars. The descriptor and canonical mapping must come from the same input. The CLI
projects its already-parsed JSON or YAML tree into this form without implementing policy;
other adapters can use the same approach.

For native standards, omitting the descriptor leaves source coverage unknown. Cross-standard
comparisons fail. Mapping errors, missing models, duplicate semantic identifiers or constraint
kinds, and unsupported canonical snapshots return `Completed = false` with diagnostics.

Cancellation returns `CMP130` with failed completion. A null API input wrapper is a programmer
error and throws `ArgumentNullException`.

Read both completion and outcome before treating a result as compatible. The result also
contains the direction, policy, typed evidence and findings, with paths, classifications and
explanations. Diagnostics identify the affected input.

Findings sort by ordinal path, rule and typed change name. Diagnostics sort by input, path
and code. Collections are read-only, and typed values use the differ's immutable snapshots.
A source-coverage finding does not invent a typed change where none exists.

These coverage checks protect comparison. They do not complete the broader
inspection/source-location work tracked in #525.
