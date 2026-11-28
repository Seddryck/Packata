---
title: Dataset validation
parent: Guides
permalink: /dataset-validation/
---

<!-- cli:data validate -->

Validate the structure and values of data bound to an ODCS or DPv2 contract.

`packata data validate <SOURCE> [--input-format auto|odcs|datapackage] [--asset <ID>] [--structure] [--constraint] [--output-format text|json]`

```shell
packata data validate contract.yaml
packata data validate contract.yaml --structure
packata data validate datapackage.json --asset orders --constraint --output-format json
packata data validate contract.yaml --structure --constraint
```

| Options | Checks |
| --- | --- |
| Neither flag | Structure and constraints |
| `--structure` | Structure only |
| `--constraint` | Constraints only |
| Both flags | Structure and constraints |

The command checks each CSV/TSV/PSV or Parquet input separately, using existing
contract validation, mapping, and reader APIs. It compares original property names
and order before contract projection. `physicalName` takes precedence over `name`;
comparison is case-sensitive. Duplicate names fail membership and make ordered
identity ambiguous. Relative resource paths and archive resources use the same
resolution as `data preview`. Multiple endpoint bindings require explicit resolution;
the validator reports unavailable binding evidence instead of choosing one.

ODCS defaults to forbidding undeclared properties and enforcing the relative order
of present declared properties. A column is mandatory only when its property has
`required: true`; omitted/false permits absence, independently of nullability. Use the
[Packata property requirements]({{ '/property-matching/' | relative_url }})
`allowAdditionalProperties` and `enforcePropertyOrder` booleans to change these policies. DPv2 uses its native `fieldsMatch`, defaulting to `exact`.
Parquet native schema types are compared with declared logical types. CSV text
does not provide authoritative logical-type metadata, so it does not receive
a fabricated schema-type verdict from sampled values.

Relationship structure checks require the source and referenced target columns to
exist and their key types to be compatible. Descriptor validation checks declaration
references and composite-key shape. Runtime structural checks never compare key
values. Constraint validation checks whether each applicable source key tuple has a target match.
Unavailable/unreadable targets, unsupported types, and ambiguous structure remain
explicit rather than being treated as empty tables or passing checks.

An external relationship such as `countries.odcs.yaml#/schema/countries/properties/id`
does not prevent available local structural checks from running. It is retained as
an unsupported declaration, so the overall report is incomplete (exit 4),
even when local presence/order checks pass. The command does not automatically
open another contract or fetch external data. Other invalid declarations still stop
the run before data access. Standalone `contract validate` keeps its strict offline
validation behavior for unresolved external references.

Constraint checks read raw values and evaluate supported logical types, formats,
required values, bounds, lengths, patterns, allowed values, unique and primary
keys, and bound foreign keys. The [capability table]({{ '/reference/dotnet/rule-execution/#executable-schema-constraints' | relative_url }})
describes parsing, nulls, unsupported declarations, and scan limits. Authored
quality rules run through `quality run`.

Structure-only mode uses the existing CSV operation to initialize original
header metadata with at most one data record. It skips value scans. For Parquet,
it uses the metadata-only schema extractor and skips row groups. Constraint-only
mode uses those same observations to bind columns and relationship prerequisites.
An unavailable required column or lookup makes the affected value check incomplete.
An invalid or null cell in an available column can produce a failed value check.
Respect the declared dialect, including the line terminator (CRLF by default).
Explicitly headerless inputs, or inputs with unavailable original schema, yield
incomplete checks. Empty/header-only inputs are checked when the reader supplies
original metadata.

| Exit | Meaning |
| --- | --- |
| 0 | All applicable checks passed completely |
| 1 | Completed violations or invalid contract/selection |
| 2 | Invalid command arguments |
| 3 | Input acquisition/execution error |
| 4 | Unsupported or unavailable evidence, or exhausted scan limits |
| 130 | Cancellation |

JSON preserves the shared `RuleEvaluationReport`, including outcomes, compilation
findings, completeness, bounded evidence, and source input identity. Result
descriptors use backend packata.structure or packata.constraint, and the text
report identifies each check and shows value-violation counts. A run may
contain both violations and incomplete checks; it never becomes an overall pass.

In .NET, `DatasetValidationService` binds existing readers to the shared runner.
Its options overload selects structure, constraints, or both. The observation-based
overload accepts externally supplied `DatasetStructure`
instances without loading descriptors or opening files. Null `Columns` means
unavailable evidence; an explicit empty list represents a known empty field set.
`StructuralValidationCompiler` produces Constraint-category plans from those
observations. ConstraintCompiler supplies value plans to the same runner.

<!-- end-cli -->
