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

Supported logical types are string, integer, number, boolean, date, time,
timestamp/datetime, object, and array. Decimal precision/scale, temporal bound
implications, maps, vectors, arbitrary pattern implications, JSON Schema
implications, and custom semantics are not inferred. Numeric bounds use finite
decimal values; unsupported values produce unknown. Constraint interactions are
assessed conservatively: a weaker bound can be breaking even if another
constraint would restrict the same values.

ODCS uses existing mapper defaults: omitted `required` and `nullable` are false,
and those are independent guarantees. Data Package `required` also rejects
null/missing values: comparison derives nullability from requiredness rather
than using the canonical mapper's default false.

Matching reuses `ContractDiffer`: dataset IDs and field names are case-sensitive.
Identifier changes remain removal/addition pairs, including ambiguous renames.
There is no rename inference. Array item definition additions/removals are unknown.
One `PropertyModified` can produce several findings for independent facets,
each retaining the original typed change and its own path/rule/explanation.

Original descriptor members are checked against a conservative coverage
allowlist in Core. Unknown members ignored by native deserializers produce
`CMP020` findings, including nested members. Their presence in both inputs does
not establish compatibility. Some native extension objects cannot be snapshotted
by the existing factual differ; these yield failed analysis instead of an empty
compatible result. Normalized SLA/lifecycle changes absent from the factual
differ are explicitly reported as unknown.

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

Descriptors are trees of `IReadOnlyDictionary<string, object?>`, object lists,
and scalars. The canonical mapping and descriptor must come from the same input.
The CLI projects its already-parsed JSON/YAML tree without implementing policy;
other adapters can make the same transport projection. Omitting the descriptor
for native standards yields unknown coverage. Cross-standard comparisons fail.
Mapping errors, missing models, duplicate semantic identifiers/constraint kinds,
and unsupported canonical snapshots produce `Completed = false` and diagnostics.
Cancellation yields `CMP130` and failed completion. Null API input wrappers are
programmer errors and throw `ArgumentNullException`.

Results expose direction, policy, completion, outcome, typed evidence, paths,
classifications, explanations, and input-attributed diagnostics. Findings sort
by ordinal path, rule, and typed change name; diagnostics sort by input, path,
and code. Result collections are read-only; typed values use the differ's
immutable snapshots. Source-coverage findings have no fabricated typed change.

These coverage checks protect comparison. They do not complete the broader
inspection/source-location work tracked in #525.
