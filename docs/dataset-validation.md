---
title: Dataset structural validation
parent: Guides
permalink: /dataset-validation/
---

<!-- cli:data validate -->

Validate the structure of data bound to an ODCS or DPv2 contract:

`packata data validate <SOURCE> [--input-format auto|odcs|datapackage] [--asset <ID>] [--output-format text|json]`

```shell
packata data validate contract.yaml
packata data validate datapackage.json --asset orders --output-format json
```

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
values: a structurally valid relationship does not prove referential integrity.
Unavailable/unreadable targets, unsupported types, and ambiguous structure remain
explicit rather than being treated as empty tables or passing checks.

An external relationship such as `countries.odcs.yaml#/schema/countries/properties/id`
does not prevent available local structural checks from running. It is retained as
an unsupported compilation finding, so the overall report is incomplete (exit 4),
even when local presence/order checks pass. The command does not automatically
open another contract or fetch external data. Other invalid declarations still stop
the run before data access. Standalone `contract validate` keeps its strict offline
validation behavior for unresolved external references.

This is structural validation. It does not execute per-cell logical-type checks,
missing/null cell constraints, bounds, uniqueness, foreign-key value membership, authored quality
rules, or SLA checks. Those value rules belong to the separate constraint compiler
(issue #597). A passed structural report is not a claim that all cell values are valid.

CSV uses the existing reader operation to initialize original header metadata with
at most one data record, without materializing its values. Parquet reuses the existing
metadata-only schema extractor and does not open row groups. Respect the declared dialect,
including line terminator (the current delimited reader defaults to CRLF).
An explicit headerless input or an input whose original header/schema cannot be
obtained produces an incomplete result. Empty/header-only inputs are checked only
when the reader supplies original metadata; no names are filled in from the contract.

| Exit | Meaning |
| --- | --- |
| 0 | All applicable structural checks passed completely |
| 1 | Completed structural violations, or invalid contract/selection |
| 2 | Invalid command arguments |
| 3 | Input acquisition/execution error |
| 4 | Unsupported or unavailable structural evidence; incomplete report |
| 130 | Cancellation |

JSON preserves the shared `RuleEvaluationReport`, including outcomes, compilation
findings, completeness, bounded evidence, and source input identity. A run may
contain both violations and incomplete checks; it never becomes an overall pass.

In .NET, `DatasetValidationService` binds existing readers to the shared runner.
Its observation-based overload accepts externally supplied `DatasetStructure`
instances without loading descriptors or opening files. Null `Columns` means
unavailable evidence; an explicit empty list represents a known empty field set.
`StructuralValidationCompiler` produces Constraint-category plans from those
observations. It does not require a separate inspector service or value compiler.

<!-- end-cli -->
