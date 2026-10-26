---
title: Contract compatibility
nav_order: 3
nav_exclude: false
permalink: /compatibility/
parent: Concepts
---

# Contract compatibility

When a dataset changes, existing consumers may still rely on the old contract.
An application might expect a `name` field to be present or an `amount` to be an
integer. Compatibility analysis asks whether the proposed contract still makes
the promises those consumers depend on.

For example, removing `name` could break an application that reads it. Changing
`amount` from integer to number could introduce fractional values that an integer
consumer cannot handle. Adding a new field is safe under Packata's policy because
consumers are assumed to tolerate extra fields.

## Compare from the consumer's point of view

`packata contract compare <BASELINE> <CANDIDATE>` uses the previous contract as the
baseline and the proposed contract as the candidate. It asks whether consumers
built for the baseline can use data described by the candidate.

Direction matters. If the old contract allows any number and the new one promises
only integers, the new values still fit the old expectation. Reversing the change
would allow fractions where consumers expect integers. Swap the arguments only
when you intend to ask that reverse question.

The analysis reads declarations in the contracts. It does not read dataset rows,
connect to a database, or test a consumer application. It also does not ask whether
a new producer accepts every input that an old producer accepted.

The [contract guide]({{ '/guides/contracts/' | relative_url }}#check-whether-a-change-could-break-consumers)
provides a runnable example. Use `contract diff` when you only want a list of what
changed, without a judgment about consumers.

## Policy: baseline-consumers-v1

The policy assumes consumers identify datasets and fields by logical name and
ignore additional fields or datasets. This is the open-record assumption. Within
that model, stronger guarantees are safe, while weaker guarantees can break
existing consumers.

For example, making a required field optional weakens the guarantee that it will
be present. Making it required strengthens that guarantee. Similarly, narrowing
an allowed-value set gives consumers fewer possibilities to handle; expanding it
may introduce values they do not understand.

### Read the classifications

| Classification | Meaning |
| --- | --- |
| `nonBreaking` | The change preserves the consumer's expectations under the policy's assumptions |
| `breaking` | The proposed contract removes or weakens a guarantee that consumers may rely on |
| `potentiallyBreaking` | The effect depends on details such as how consumers access the data |
| `unknown` | Packata cannot establish compatibility for this declaration or change |

Unknown is not a safe result. It can occur even when two inputs look identical,
for example when both contain a rule the analysis does not understand. The
absence of a detected difference does not establish that such a rule is covered.

### Field and metadata rules

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

Requiredness and nullability answer different questions: must the field be present,
and may its value be null? In ODCS, omitted `required` and `nullable` both default
to false. Data Package requiredness also rejects null or missing values, so the
analysis derives Data Package nullability from requiredness rather than using
the shared mapper's default of false.

### Understand the limits of the rules

The policy supports strings, integers, numbers, booleans, dates, times, timestamps,
datetimes, objects and arrays. It does not reason about decimal precision/scale,
temporal bounds, maps, vectors, arbitrary patterns, JSON Schema or custom semantics.
Numeric bounds must be finite decimal values; unsupported bounds produce unknown
findings.

Constraints are checked conservatively. Packata does not prove that combinations
of constraints make every apparent weakening safe. For example, lowering a
minimum can be classified as breaking even if another constraint would still
exclude the newly permitted values.

Dataset IDs and field names are matched case-sensitively, following `ContractDiffer`.
A renamed identifier is a removal plus an addition, not an inferred rename.
Adding or removing an array item definition is classified as unknown.

## Distinguish a completed report from a compatible result

A comparison can finish and still report breaking, potentially breaking or unknown
findings. Its overall outcome follows the most severe finding: breaking first,
then potentiallyBreaking, then unknown. Otherwise the outcome is compatible.
Every finding remains in the report, including those below the overall severity.

A single change can produce several findings. For example, one `PropertyModified`
change can affect both presence and value guarantees. Each finding includes its
own path, rule and explanation alongside the original typed change.

The CLI can use these classifications to decide its exit code. That gate is
separate from the analysis itself: `--fail-on none` allows a completed report to
exit successfully while retaining breaking or unknown findings. See the
[compare reference]({{ '/cli/' | relative_url }}#assess-consumer-compatibility)
for defaults, output fields and exit codes.

### Check what the analysis covered

Packata checks the source descriptor against the members its analysis understands.
Unrecognized members produce `CMP020` findings, including nested properties that
a native deserializer might otherwise ignore. The same unsupported member in
both inputs does not establish compatibility.

A finding about unsupported source content may have no typed diff change.
Similarly, a normalized SLA or lifecycle change missing from the factual diff is
reported as unknown. Some native extension objects cannot be captured by the
differ at all; those cause failed analysis rather than an empty compatible report.

## Reusable API

In .NET, you can compare two contracts already in the shared model:

```csharp
CompatibilityResult result = ContractCompatibility.Compare(previous, current);
foreach (var finding in result.Findings)
    Console.WriteLine($"{finding.Classification} {finding.Path}: {finding.Explanation}");
```

This overload applies the shared model's assumptions. When your inputs originate
in ODCS or Data Package, pass their source standard, mapping diagnostics and
original descriptor too, so the analysis can check source coverage:

```csharp
var baseline = new CompatibilityInput(previousMapping.Value, ContractStandard.Odcs,
    previousMapping.Diagnostics, previousDescriptor);
var candidate = new CompatibilityInput(currentMapping.Value, ContractStandard.Odcs,
    currentMapping.Diagnostics, currentDescriptor);
CompatibilityResult result = ContractCompatibility.Compare(baseline, candidate, cancellationToken);
```

Each descriptor must be a tree of `IReadOnlyDictionary<string, object?>` values,
object lists and scalars. It must come from the same input as its mapped contract.
The CLI constructs this tree from its parsed JSON or YAML; other adapters can do
the same without implementing a separate compatibility policy.

### Handle incomplete or failed analysis

For native standards, omitting the descriptor leaves source coverage unknown.
Comparing different source standards fails. Mapping errors, missing models,
duplicate semantic identifiers or constraint kinds, and unsupported snapshots
return `Completed = false` with diagnostics.

Cancellation also returns failed completion, with `CMP130`. A null API input
wrapper is a programming error and throws `ArgumentNullException`.

Read both completion and outcome before treating the result as compatible. The
result includes direction, policy and findings with paths, classifications,
explanations and typed evidence. Diagnostics identify which input caused a problem.

Findings sort by ordinal path, rule and typed change name; diagnostics sort by
input, path and code. Collections are read-only, and typed values are immutable
diff snapshots. A source-coverage finding does not invent a typed change when
none exists.

These checks cover compatibility analysis. Broader inspection and source-location
work remains tracked separately in #525.
