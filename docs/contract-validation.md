---
title: Descriptor validation
parent: Contracts and standards
nav_order: 3
permalink: /standards/validation/
---

`contract validate` checks declarations, without opening datasets, connecting to
servers, executing quality rules, or evaluating SLA compliance.

```console
packata contract validate contract.yaml                     # schema + semantic
packata contract validate contract.yaml --schema            # schema only
packata contract validate contract.yaml --semantic          # semantic only
packata contract validate contract.yaml --semantic --schema  # both, in schema-first order
```

Flags are additive. With any layer flag, only the requested layers run. With no
flags, the explicit default is schema plus semantic; future categories do not
automatically enter that default. A schema failure skips semantic validation with
a reason. Semantic-only validation never enables schema validation implicitly.

JSON reports contain `layers` (the selected layers, each with `state`, `executed`
and optional `reason`) and `diagnostics` (`layer`, `code`, `severity`, `sourcePath`,
`message`). States are `Passed`, `Failed`, and `Skipped`. Success requires **every
selected layer to complete and pass**. Parse/version prerequisites produce skipped
layers and errors; an incomplete native declaration check also produces an error.
New source paths use `$` followed by a JSON Pointer; reused native diagnostics
retain their existing paths. Schema messages name the selected profile and explain
the expected shape or failed assertion without echoing source values. Rejected
`anyOf`/`oneOf` alternatives and false `if` conditions are not independent errors.
When shape or discriminator evidence identifies one relevant alternative, its
actionable findings replace ancestor summaries; otherwise a union-shape error is
reported. Multiple matching `oneOf` alternatives produce an ambiguity error.
Independent failed assertions remain visible. Exit codes remain `0` success, `1` invalid or unsupported
descriptor, `2` invocation error, `3` I/O failure, `130` cancellation.

## Schemas and versions

Validation uses the original JSON or a typed YAML 1.2 core projection before native
defaults, coercion, or ignored properties. Quoted scalars remain strings, explicit
null remains null, duplicate keys fail, and cyclic aliases, multiple documents,
depth over 64, and expansions over 100,000 nodes fail parsing. Non-finite YAML
numbers and unsupported explicit tags are rejected. JSON input uses JSON syntax.

| Standard | Selection and pinned artifact |
| --- | --- |
| ODCS | Only an explicit `apiVersion: v3.2.0` is supported. Missing or other versions fail selection, even though the companion schema lists older versions. Contract `version` is independent. Uses the unmodified [official companion schema](https://github.com/bitol-io/open-data-contract-standard/blob/f0bdad95346905d500be5ef4b2c2d9b1d95223b7/schema/odcs-json-schema-v3.2.0.json), release commit `f0bdad95346905d500be5ef4b2c2d9b1d95223b7`, Draft 2019-09, with format assertions explicitly enabled. |
| Data Package | Defaults to the strict official v2 profile when `$schema` is absent. The [official v2 profile](https://datapackage.org/profiles/2.0/datapackage.json) is embedded as downloaded, using Draft 7 and format assertions. |
| Packata Category Tables 1.0.0 | Explicit `$schema: https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/docs/profiles/category-tables/1.0.0/datapackage.json` selects the self-contained Draft 7 compatibility profile. It adds reference objects and category field maps while retaining inline categories and other v2 assertions. See the [complete recipe example]({{ '/datapackage-categories/' | relative_url }}#category-tables-recipe). |

The embedded artifacts are offline resources. Descriptor `$schema`, references,
and endpoint declarations cannot trigger schema downloads. Automatic detection
uses ODCS `kind`/`apiVersion` or Data Package content; mixed ODCS markers and
`resources` are ambiguous. `--input-format odcs|datapackage` overrides detection,
but does not supply required source fields or a missing ODCS specification version.

An unknown or malformed `$schema` fails selection. Reports include `profile`
(`uri`, `label`, `format`) for the selected artifact, also shown in CLI text and
the VS Code validation output. The recipe profile checks descriptor structure;
semantic checks additionally verify local resource/field bindings, type agreement,
required and unique codes, unique string labels and label encoding prerequisites.
Neither layer reads lookup rows or resolves external packages.

## Semantic checks

This is the implementation matrix for ODCS 3.2.0. “ODCS” refers to declaration
rules and consistency derived from the [versioned specification](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/).
“Packata” identifies interoperability or execution-model requirements. Organizational
recommendations (preferred status, classification, role, naming or ownership
vocabularies) are not enforced as normative requirements.

| Area | Semantic check beyond the official schema | Origin / limits |
| --- | --- | --- |
| Structure and names | Explicit `schema`; non-empty identity/version/kind and existing status requirement; non-empty, scoped unique schema/property names, server names, role names and team usernames | ODCS declaration consistency; schema itself does **not** require `schema` or `status`. Empty schema arrays remain permitted. |
| Stable IDs | Reserved characters and duplicate IDs in containing declaration arrays, including properties, relationships, quality, enums, governance, references and custom properties | ODCS; IDs may repeat in different scopes. Root contract ID is not a stable element ID. Vendor payload contents are opaque. |
| Relationships | Schema-level `from`/`to` shapes, implicit property-level sources, equal nonzero composite lengths, no repeated references, existence and matching declared logical types/type options | ODCS; shorthand uses names (`users.id`), qualified paths use IDs (`schema/users-id/properties/key-id`), including nested properties. External `file.yaml#…`/URL references fail with an explicit unresolved-reference error; no resolver or network fetch is implied. Missing logical types are not inferred. |
| Keys and partitions | Supplied positions must be unique positive integers on flagged sibling properties; `-1` means unspecified | ODCS; positions are optional, and gaps/partially specified positions are allowed. |
| Options and bounds | Numeric/temporal bounds must match the declared type and describe a nonempty interval; length/item/property limits must be non-negative integers with minimum ≤ maximum; `multipleOf` positive; decimal precision positive, scale non-negative and ≤ declared precision | ODCS consistency plus Packata decimal support. Options without a logical type cannot be meaningfully checked and fail. Native decimal/range limits still apply. |
| Enums and vectors | Nonempty enum with unique **values**, independent of metadata; non-null scalar values match the logical type without numeric/string coercion; vectors require positive integer dimensions | ODCS plus Packata's non-null enum model. Scalar range support follows the native model. |
| Servers | Unique declarations, supported encoding placement, local directory servers require object physical names | ODCS plus Packata binding rules. Multiple servers do not acquire inferred asset bindings. No endpoint availability or authentication checks. |
| Governance | Scoped IDs/names, team joining date ≤ leaving date, replacement identifies a different declared member | Declaration consistency. No IAM, email ownership, or organizational policy evaluation. |
| Quality | Type-specific definition, known library metric, custom implementation presence, metric/deprecated-rule conflicts, expectations on measured rules, numeric ordering operands and ascending two-value ranges | ODCS declaration consistency. Custom adapter/normalization limitations remain warnings. No SQL/expression execution, scheduler validation or metric measurement. |
| SLA | Existing duration/unit, availability percentage/window, element-path and lifecycle milestone normalization; invalid recognized declarations are errors | Packata normalization rules; unsupported SLA property names remain warnings and metadata. No compliance evaluation. |
| Category tables | Retains category declarations, field-map, referenced object/property and binding checks (`ODCS006`/`ODCS007`) | **Packata extension**, declared through `vendor: packata`; no lookup dataset reads. |

The schema layer executes every assertion in the pinned artifact, including closure,
conditional branches, types, formats, enums and collection constraints. The
[complete assertion-path inventory]({{ '/odcs-schema-assertions.tsv' | relative_url }})
records the exact keywords/values delegated to the schema engine. Regression tests
cover both source representations, selection, layer sequencing, declarations and
prerequisites (`DescriptorValidationTests`, `ValidateCommandTests`).

### Deliberate specification/schema differences

The official artifact is never patched to make the semantic layer agree with it:

| Disagreement | Schema-only | Semantic-only / combined |
| --- | --- | --- |
| Required schema section | May omit `schema` | Semantic requires it; combined catches it after schema passes. |
| Optional property `logicalType` | Optional; upstream conditionals can still constrain options when absent | May omit type without options; supplied options require a type. |
| Object-valued array items | Uses `SchemaItemProperty`, allowing nested object properties | Recursively checks the declared item object and its properties. |
| Vector without options | Can pass because dimensions is not conditionally required | Fails `SEM_VECTOR`. |
| Enum entries with equal values and different labels | `uniqueItems` compares complete entries, so may pass | Fails `SEM_ENUM`. |
| Enum value type / null | Companion schema scalar union may accept mismatched values and null | Enforces logical type without coercion and rejects null; `required` is not a null enum entry. Explicit null elsewhere follows the schema's declared types. |
| Decimal alias / `nullable` extension | `decimal` and property `nullable` are outside the official ODCS shape | Existing native decimal alias/nullability support does not override schema failure in combined mode. |

For example, `logicalType: vector` without options passes schema-only but fails
semantic-only and default validation. Adding `logicalTypeOptions: {dimensions: 3}`
fixes that declaration. Adding an unknown root field fails schema validation and
skips semantics. A valid descriptor still says nothing about actual vector lengths,
enum membership, foreign-key rows, quality results, or SLA observations in a dataset.

## Library entry point

The CLI and .NET hosts share `Packata.Documents.Validation.DescriptorValidation`:

```csharp
var report = DescriptorValidation.Validate(originalText, format: "odcs");
var semantics = DescriptorValidation.Validate(originalText, format: "odcs",
    layers: ValidationLayers.Semantic);
// report.Valid requires every selected layer to complete and pass.
```

Pass original source text, not a serialized native model. The older
`ContractDocument.Validate(object)` remains a native-model helper; it cannot prove
source-schema validity. Data Package semantic validation retains the existing
resource, field, key/category, contributor/license and dialect validators.
