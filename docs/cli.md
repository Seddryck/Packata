---
title: Command-line interface
nav_order: 1
nav_exclude: false
permalink: /cli/
parent: Reference
has_children: true
---

# Packata command-line interface

Install the .NET tool with `dotnet tool install --global Packata-cli`. Run
`packata --help` or `packata --version` to discover commands and the installed version.
The existing `packata infer` command remains available.

## Command index

| Command | Reference |
| --- | --- |
| `contract validate` | [Validate](#validate-a-contract) |
| `contract inspect` | [Inspect](#inspect-the-canonical-contract) |
| `contract diff` | [Factual differences](#compare-two-contract-versions) |
| `contract compare` | [Consumer compatibility](#assess-consumer-compatibility) |
| `schema export`, alias `export` | [JSON Schema](#export-a-dataset-schema) |
| `schema infer`, alias `infer` | [Schema inference]({{ '/cli-inference/' | relative_url }}) |
| `data preview` | [Read data](#preview-bound-data) |
| `dialect export` | [CSV dialect](#export-a-csv-dialect) |
| `connector list`, `connector show`, `connector install` | [Connectors]({{ '/cli-database/' | relative_url }}) |
| `config list`, `config get`, `config set`, `config unset` | [Configuration]({{ '/cli-database/' | relative_url }}) |

Use `--help`, `-h` or `-?` for help. Root version switches are `--version` and `-v`. Branch-only invocations show help. Quote arguments with spaces for your shell.

## Exit-code families

| Commands | Codes |
| --- | --- |
| validate, inspect, diff, schema export/export, dialect export, data preview | 0 success; 1 invalid/unsupported document or operation; 2 invocation error; 3 I/O failure; 130 cancellation |
| contract compare | Same base codes, plus 4 for a completed report failing the selected gate and 5 for incomplete analysis; see [details](#assess-consumer-compatibility) |
| schema infer/infer | 0 success; 1 operation failure; 2 caught cancellation or invocation error; framework cancellation can return 130 |
| config and connector discovery | 0 success; 1 operation/file failure; 2 invocation error |
| connector install | 0 success; 1 operation failure; 2 caught cancellation/invocation error; framework cancellation can return 130 |

## Shared options

The CLI uses Spectre.Console.Cli. Commands are grouped under `contract`, `schema`,
`data`, `dialect`, `connector`, and `config`. `infer` and `export` remain compatibility
entry points for `schema infer` and `schema export`, with the same options.

| Option | Meaning |
| --- | --- |
| `--input-format auto\|odcs\|datapackage` | Interpret the source contract; supported by every contract-reading command |
| `--output-format` | Select the generated representation; allowed values depend on the command |
| `--output <PATH>`, `-o <PATH>` | Destination file, never a representation name |
| `--asset <ID>` | Exact Data Package resource name or ODCS schema object name |

Validation, inspection, diff, compatibility comparison, preview, config list/get, and connector list/show
can save their normal result using `--output`. Existing files are protected and
file output has no ANSI styling. File output is committed only after rendering.
Diagnostics remain on stderr (and retain their existing inclusion in JSON results).
`schema infer --output` writes an enriched contract in the source standard, as before;
its interactive inference report still goes to stdout. It has no separate output-format
option because it preserves the source document representation. `--write` updates
that source in place and cannot be combined with `--output`.

Use only the documented option names. The current parser can ignore unrecognized tokens, so a successful exit alone does not prove an unsupported flag was applied. `--output` always means a file path, not a representation. Export also accepts `-f` for `--output-format`. For diff and compare, `--input-format` sets both source standards; `--baseline-format` and `--candidate-format` override it independently, including explicit `auto`.

```console
packata contract inspect contract.yaml --input-format odcs --output-format json -o inspection.json
packata schema export contract.yaml --asset customers --output-format json-schema -o customers.schema.json
packata data preview datapackage.json --asset customers --output-format csv -o customers.csv
```


<!-- cli:dialect export -->
## Export a CSV dialect

`packata dialect export <SOURCE> --endpoint <ID> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--endpoint <ID>` | Required | Exact CSV endpoint identifier |
| `--output-format <FORMAT>` | `json` only; `json` | Standalone Table Dialect JSON |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |
| `--overwrite` | Off | Requires `--output`; permits replacing output but never the source |

All shown positional arguments are required. Use `--help` for command help.

```console
packata dialect export datapackage.json --endpoint customers-endpoint
packata dialect export contract.yaml --endpoint local --output-format json --output customers.dialect.json
```

Export a selected CSV endpoint as standalone Data Package v2 Table Dialect JSON.
Use `contract inspect` to find the endpoint ID. `--endpoint` is required; without
`--output` the descriptor goes to stdout. Diagnostics go to stderr. Replacing an
existing output file requires `--overwrite`; the source contract is protected.
Export reads contract metadata without opening CSV data. See
[CSV dialect export]({{ '/csv-dialect-export/' | relative_url }}) for the library API, supported
settings, defaults, diagnostics, and an example with two endpoint dialects.

<!-- cli:contract validate -->
## Validate a contract

`packata contract validate <SOURCE> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--output-format <FORMAT>` | `text`, `json`; `text` | Result representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |

All shown positional arguments are required. Use `--help` for command help.

```console
packata contract validate contract.yaml
packata contract validate datapackage.json --output-format json
packata contract validate descriptor --input-format odcs
```

Validation accepts ODCS v3.2.0 YAML and Data Package v2 JSON/YAML. Detection examines
the document root; `--input-format auto|odcs|datapackage` overrides standard detection.
Local files, HTTP(S) descriptors, and local/HTTP(S) ZIP packages are supported.
A ZIP must contain exactly one descriptor named `datapackage.json`,
`datapackage.yaml`, `datapackage.yml`, `contract.yaml`, or `contract.yml`, including
inside a subdirectory. Validation never opens referenced data resources.

Data Package validation uses the library's descriptor validator. ODCS validation
uses the library's YAML/semantic validation plus checks for required identity,
version, status, supported API version, and document kind.

`--output-format text` is the default. Text diagnostics go to stderr. `--output-format json`
writes a single object to stdout with `schemaVersion: 1`, `source`, `valid`, and
`diagnostics`. Each diagnostic has `code`, `severity`, `sourcePath`, and `message`.
Diagnostics do not include raw parser snippets or stack traces; connection secrets
are redacted. Redirected output has no ANSI styling.

Exit codes for these new commands: `0` success, `1` invalid document or operation,
`2` invocation error, `3` I/O failure, and `130` cancellation. These do not change
the existing `infer` command's exit-code contract.

<!-- cli:contract inspect -->
## Inspect the canonical contract

`packata contract inspect <SOURCE> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--output-format <FORMAT>` | `text`, `json`; `text` | Result representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |
| `--asset <ID>` | All assets | Optional exact asset identifier |

All shown positional arguments are required. Use `--help` for command help.

```console
packata contract inspect contract.yaml
packata contract inspect datapackage.json --asset customers --output-format json
```

Inspection uses the same source loading and `--input-format` options as validation.
Text output displays identity, metadata, assets, schema fields and constraints,
endpoint bindings, locations, formats, and governance as a tree. Mapping warnings
remain visible on stderr. `--asset` selects an exact canonical identifier and
restricts the result to that asset and its referenced endpoints; unknown or
ambiguous identifiers fail.

JSON is a single object with `schemaVersion: 1`, `source`, `contract`, and
`diagnostics`. `contract` contains `identity`, `metadata`, `assets`, `endpoints`,
`governance`, and `extensions`, using camelCase property names and string enum
values. Schema fields/constraints and endpoint bindings are nested under each
asset. Diagnostics use the same four fields as validation. Credentials and
connection URLs are redacted, including nested extension metadata.

<!-- cli:contract diff -->
## Compare two contract versions

`packata contract diff <BASELINE> <CANDIDATE> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--baseline-format <FORMAT>` | Inherits `--input-format` | Per-baseline override; explicit `auto` restores detection |
| `--candidate-format <FORMAT>` | Inherits `--input-format` | Per-candidate override; explicit `auto` restores detection |
| `--output-format <FORMAT>` | `text`, `json`; `text` | Result representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |
| `--quiet` | Off | Suppress results and file creation; retain stderr diagnostics |

All shown positional arguments are required. Use `--help` for command help.

```console
packata contract diff sales-v1.odcs.yaml sales-v2.odcs.yaml
packata contract diff old/datapackage.json new/datapackage.json --output-format json
packata contract diff old/descriptor new/descriptor --baseline-format odcs --candidate-format datapackage
packata contract diff old.zip https://example.test/new.zip --quiet
```

The **baseline is previous** and the **candidate is current**. An addition is present
only in the candidate, a removal only in the baseline, and a modification retains
baseline-to-candidate values. Differences are factual data; this command does not
apply a compatibility policy or decide whether a change is breaking.

Each input is independently detected, validated, and mapped using the inspection
command's descriptor loader. Local, HTTP(S), and local/remote ZIP sources are
supported. Referenced datasets are never opened, databases are never connected,
and connectors are never acquired or configured. The command does not write either input.

ODCS-to-ODCS and Data Package v2-to-Data Package v2 comparisons use the canonical
library's `DataContract.Diff` API. Cross-standard comparisons also use that shared
model directly, including preserved extension metadata; there is no CLI-specific
matching or normalization. Differences outside the canonical mapping are not
compared. The ODCS mapper populates SLA/lifecycle commitments and typed value domains; native quality rules are not populated as canonical executable rules. Compare only what mapping preserves; compatibility coverage is assessed separately by `contract compare`.
Unsupported source formats, values the library cannot snapshot (such as custom
objects in extension metadata), duplicate semantic identities, and unknown library
change kinds fail explicitly rather than returning an empty successful diff.

Text output names both inputs, states `baseline -> candidate`, then prints each
change's operation, asset, semantic path, concrete kind, and payload values. An
empty result says `No differences detected.` Library ordering is preserved.
Redirected text and JSON contain no ANSI styling. Text diagnostics go to stderr.

JSON stdout contains exactly one object, with this versioned shape:

```json
{
  "schemaVersion": 1,
  "baseline": "sales-v1.odcs.yaml",
  "candidate": "sales-v2.odcs.yaml",
  "hasChanges": true,
  "changes": [
    {
      "kind": "PropertyTypeChanged",
      "operation": "modified",
      "asset": "property",
      "path": "schema.orders.properties.amount.type",
      "datasetId": "orders",
      "propertyId": "amount",
      "previous": "integer",
      "current": "number"
    }
  ],
  "diagnostics": []
}
```

Every change has `kind`, `operation` (`added`, `removed`, or `modified`), `asset`,
and the library's semantic `path`. The following additional fields are emitted
at the same level; fields for other kinds are absent. Applicable values retain
explicit JSON nulls, so `"previous": null` differs from an absent `previous` field.

| Kind | Asset | Additional fields |
| --- | --- | --- |
| `ServerAdded`, `ServerRemoved` | `server` | `server`: canonical endpoint |
| `ServerModified` | `server` | `previous`, `current`: canonical endpoints |
| `SlaAdded`, `SlaRemoved` | `sla` | `sla`: canonical service level |
| `SlaModified` | `sla` | `previous`, `current`: canonical service levels |
| `DatasetAdded`, `DatasetRemoved` | `dataset` | `dataset`: canonical asset including its subtree |
| `DatasetModified` | `dataset` | `previous`, `current`: canonical assets |
| `PropertyAdded`, `PropertyRemoved` | `property` | `datasetId`, `propertyId`, `property`: canonical field |
| `PropertyModified` | `property` | `datasetId`, `propertyId`, `previous`, `current`: canonical fields |
| `PropertyTypeChanged` | `property` | `datasetId`, `propertyId`, `previous`, `current`: logical-type strings or null |
| `RelationshipAdded`, `RelationshipRemoved` | `relationship` | `relationship`: canonical relationship |
| `RelationshipModified` | `relationship` | `previous`, `current`: canonical relationships |
| `QualityRuleAdded`, `QualityRuleRemoved` | `qualityRule` | `qualityRule`: canonical quality rule |
| `QualityRuleModified` | `qualityRule` | `previous`, `current`: canonical quality rules |
| `ContractMetadataChanged` | `contractMetadata` | `previous`, `current`: identity, metadata, governance, or extensions, according to `path` |

Payload records use camelCase property names and string enum values. Canonical
extensions are objects keyed by namespace, with each namespace's key/value map
preserved. Extension dictionary keys keep their original casing. No CLR type or
assembly metadata is added. Changes are ordered by ordinal path and concrete kind,
as returned by the library. See the README's comparison section for identity,
escaped-path, subtree, and immutable-snapshot rules.

Each diagnostic has `input` (`baseline`, `candidate`, or `comparison`), `source`,
`code`, `severity`, `sourcePath`, and `message`. Successful JSON includes warnings
in `diagnostics`; quiet mode sends them to stderr. Credentials and connection
strings are redacted in source identifiers, diagnostics, paths, descriptions,
and nested payloads in both representations. Failures emit diagnostics on stderr
and no successful or partial diff object on stdout.

Exit codes are `0` for a completed comparison, **including differences**, `1` for
an invalid/unsupported contract or comparison, `2` for invalid invocation or option
values, `3` for I/O failure, and `130` for cancellation. Automation can inspect
`hasChanges` and `changes`; compatibility gates, filters, rename heuristics, and
patch generation are not part of this command.

<!-- cli:contract compare -->
## Assess consumer compatibility

`packata contract compare <BASELINE> <CANDIDATE> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--baseline-format <FORMAT>` | Inherits `--input-format` | Per-baseline override; explicit `auto` restores detection |
| `--candidate-format <FORMAT>` | Inherits `--input-format` | Per-candidate override; explicit `auto` restores detection |
| `--output-format <FORMAT>` | `text`, `json`; `text` | Result representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |
| `--quiet` | Off | Suppress results and file creation; retain stderr diagnostics |
| `--policy <POLICY>` | `baseline-consumers-v1` only | Directional baseline-consumer policy |
| `--fail-on <CLASSIFICATIONS>` | `breaking,potentially-breaking,unknown` | Comma-separated gate classes or `none`; does not reclassify results |

All shown positional arguments are required. Use `--help` for command help.

```console
packata contract compare sales-v1.odcs.yaml sales-v2.odcs.yaml
packata contract compare old/datapackage.json new/datapackage.json --output-format json
packata contract compare previous current --baseline-format odcs --candidate-format odcs --policy baseline-consumers-v1
packata contract compare previous.yaml current.yaml --fail-on breaking,unknown --output-format json
packata contract compare previous.yaml current.yaml --fail-on none
```

The baseline is previous and the candidate is current, matching `contract diff`.
The question is whether baseline consumers can safely consume candidate data.
Only same-standard ODCS 3.2.0 and Data Package v2 comparisons are supported.
Local, HTTP(S), and ZIP descriptors use the existing loader; referenced data,
databases, and connectors are never opened. Each input's standard is detected
independently. `--input-format` selects a shared input standard; per-input format options override it.

Use `--output-format json -o compatibility.json` to save the report. Existing files, including source descriptors, are protected. `--quiet` also suppresses file creation. Completed reports are saved even when the compatibility gate fails; invalid documents and failed analyses retain their structured failure envelopes.

The [policy and backend API]({{ '/compatibility/' | relative_url }}) document directional rules,
matching, source semantics, and unsupported constructs. Integer → number is
breaking for integer consumers; number → integer strengthens the guarantee.
Additional fields are safe under the open-record assumption. `contract diff`
continues to report factual differences only.

Text output shows both sources, direction, policy, completion/outcome, findings
with classifications/paths/explanations, and classification totals. For example:

```text
Baseline (previous): previous.yaml
Candidate (current): current.yaml
Direction: baseline -> candidate; policy: baseline-consumers-v1
Analysis: breaking; completed: True; exit code: 4
breaking schema.orders.properties.amount.type: The candidate type permits logical values outside the baseline type.
Totals: {"breaking":1,"potentiallyBreaking":0,"nonBreaking":0,"unknown":0}
```

JSON uses `schemaVersion: 1`; the [JSON schema]({{ '/schemas/contract-compare-v1.schema.json' | relative_url }})
defines this envelope:

| Field | Meaning |
| --- | --- |
| baseline, candidate | Source identifiers; null on invocation parsing failures |
| direction, policy | baseline-to-candidate and baseline-consumers-v1 |
| completed | Analysis finished, independently of unknown findings |
| outcome | compatible, breaking, potentiallyBreaking, unknown, failed |
| isCompatible | True only for established, completed compatibility |
| hasChanges | Observed canonical differences; null for incomplete analysis |
| failOn, gatePassed, exitCode | CLI gate and outcome; gatePassed is null on failure |
| totals | Finding counts for all four classifications, not factual-change counts |
| findings | path, classification, rule, explanation, typed change, sourceLocation |
| diagnostics | input, source, code, severity, sourcePath, message, sourceLocation |

Typed `change` payloads reuse the documented `contract diff` kind, operation,
asset, path, and concrete values. Several findings can reference one typed
change. Coverage findings and model facets absent from the differ have
`change: null`; unsupported raw-source changes need not appear in `hasChanges`.
Current backend models provide no line/column mapping, so `sourceLocation` is
explicitly null. Coverage diagnostic paths refer to the source descriptor;
typed change paths refer to canonical assets. Sources, diagnostics, and nested
typed values are redacted.

An unchanged supported contract produces:

```json
{
  "schemaVersion": 1,
  "baseline": "previous.yaml",
  "candidate": "current.yaml",
  "direction": "baseline-to-candidate",
  "policy": "baseline-consumers-v1",
  "completed": true,
  "outcome": "compatible",
  "isCompatible": true,
  "hasChanges": false,
  "failOn": "breaking,potentially-breaking,unknown",
  "gatePassed": true,
  "exitCode": 0,
  "totals": {"breaking": 0, "potentiallyBreaking": 0, "nonBreaking": 0, "unknown": 0},
  "findings": [],
  "diagnostics": []
}
```

Invalid inputs, loading errors, cancellation, and failed analysis emit this same
JSON envelope with `completed: false`, `outcome: failed`, `isCompatible: false`,
`hasChanges: null`, `gatePassed: null`, and diagnostics. Findings can be partial.
Invalid invocations emit a failure envelope when `--output-format json`,
`--output-format=json`, or `--output-format:json` requests stdout, with null
sources; the usage error also goes to stderr. Destination options accept these
same space, equals, and colon forms with either `--output` or `-o`.
Invocation errors never create output files. Options after `--` are not used to
select presentation. An invalid output format itself cannot select JSON.
`--quiet` (including explicit `true`) suppresses every
envelope. Otherwise JSON diagnostics stay inside the envelope and ordinary logs
do not contaminate machine-readable stdout. An explicit `--quiet=false` retains
normal reporting.

| Exit code | Meaning |
| --- | --- |
| 0 | Completed analysis passes the selected gate |
| 1 | Invalid/malformed contract or descriptor package |
| 2 | Invalid invocation/options |
| 3 | Source or output I/O/access failure |
| 4 | Completed analysis contains a classification selected by --fail-on |
| 5 | Analysis/mapping cannot complete, including unsupported cross-standard comparisons |
| 130 | Cancellation |

Default gating fails for breaking, potentially breaking, and unknown findings.
`--fail-on none` enables reporting without gating; it never changes semantic
classifications or describes unknown as compatible. It cannot waive invocation,
invalid-input, I/O, analysis, or cancellation failures. JSON supplies #572 with
classifications and explanations directly. GitHub annotations, validation
line/column mapping, and MCP registration remain separate work.

<!-- cli:schema export -->
<!-- cli:export -->
## Export a dataset schema

`packata schema export <SOURCE> --asset <ID> --output-format json-schema [OPTIONS]`

`packata export <SOURCE> --asset <ID> --output-format json-schema [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--asset <ID>` | Required | Exact Data Package resource or ODCS schema object name |
| `--output-format <FORMAT>`, `-f <FORMAT>` | Required; `json-schema` | Target representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |

All shown positional arguments are required. Use `--help` for command help.

```console
packata schema export sales.odcs.yaml --output-format json-schema --asset orders
packata schema export sales.odcs.yaml -f json-schema --asset orders -o orders.schema.json
```

`export` accepts the same local, HTTP(S), and ZIP descriptors as inspection,
including ODCS and Data Package v2 documents. `--output-format` (`-f`) selects the target
representation and must be `json-schema`. `--asset` is required and selects
an exact canonical asset identifier. Missing or ambiguous identifiers fail.

The result describes one dataset record as a JSON Schema Draft 2020-12 object.
Stdout contains only the generated JSON; diagnostics go to stderr. `--output`
(`-o`) writes a new UTF-8 file instead. Existing files, including the source
contract, are protected. Export reads only the descriptor and never accesses
referenced data or installs connectors. It uses the validation command's exit codes.

String, boolean, integer, number/decimal, date, timestamp/datetime, time, array,
and object types are supported. Dates and times use JSON Schema formats. Nested
objects and array items are converted recursively. String lengths/patterns,
numeric bounds, enums, array size/uniqueness, and object size/required properties
are retained when representable. Required properties and nullability are independent:
`required: true` adds the name to its parent's `required` list; `nullable: true`
allows `null` alongside the field's type. Omitted nullability defaults to false.

Unknown types (including map and vector), invalid constraints, temporal range
constraints, and dataset-wide uniqueness/primary-key constraints produce conversion
errors. A schema for one record cannot enforce uniqueness across records. No custom
extension keywords are emitted. Physical storage details, relationships, quality
rules, and governance metadata are outside this record-schema representation.

The same converter is available in `Packata.Core.Contracts`:

```csharp
var schema = asset.ToJsonSchema();
schema.Serialize(stream);
```

<!-- cli:data preview -->
## Preview bound data

`packata data preview <SOURCE> --asset <ID> [OPTIONS]`

| Option / alias | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source standard; locations remain command-specific |
| `--asset <ID>` | Required | Exact Data Package resource or ODCS schema object name |
| `--limit <COUNT>` | Positive 32-bit integer; `20` | Maximum data rows, excluding headers |
| `--columns <NAMES>` | All fields | Unique exact names, comma-separated, in output order |
| `--output-format <FORMAT>` | `table`, `json`, `csv`; `table` | Result representation |
| `--output <PATH>`, `-o <PATH>` | Stdout when omitted | New destination file; existing files are protected |
| `--connector <SCHEME=ID>` | Repeatable; persisted preference otherwise | Invocation connector preference |
| `--connection-env <ENDPOINT=VARIABLE>` | Repeatable; omitted by default | Runtime database URL from environment; never persisted |

All shown positional arguments are required. Use `--help` for command help.

```console
packata data preview contract.yaml --asset customers --limit 20
packata data preview datapackage.json --asset customers --limit 5 --columns id,name
packata data preview contract.yaml --asset customers --output-format json > customers.json
```

The source is an unpacked ODCS YAML or Data Package v2 JSON/YAML descriptor;
`--asset` is required. Paths, physical table names, schemas, and format/dialect
metadata come from its canonical binding. Relative file paths and database files
resolve against the descriptor directory, independently of the current directory.
Missing or ambiguous assets and endpoint bindings fail instead of selecting one.
The command registers delimited/inline, Excel, Parquet, and database providers.
Database connector acquisition uses the shared configured service; a database read can download a missing connector when downloads are enabled.

`--limit` defaults to **20**, must be a positive 32-bit integer, and excludes
headers. Reads stop at that limit or end of data. `--columns` is a comma-separated
list of unique, exact field names in output order; omitted means all columns.
An invalid name, duplicate name, or ambiguous reader column fails.

`--output-format table|json|csv` defaults to `table`. `--output` (`-o`) saves the
result to a new file; omit it to write to stdout. Diagnostics go to stderr. JSON/CSV stdout contains
only the requested representation and redirected output has no ANSI styling.

JSON has the stable shape `{"schemaVersion":1,"columns":["id","name"],"rows":[[1,"Alice"]]}`.
Each row is an array corresponding to `columns`, preserving JSON nulls and
avoiding object-key collisions. Empty results retain column names when supplied
by the reader or declared schema. CSV includes a header row and quotes delimiters, quotes, and line
breaks; nulls are empty fields. Values use invariant culture, dates use round-trip
format, and binary values are base64. Table cells display null as `(null)`, flatten
line breaks, and truncate values beyond 80 characters; terminal layout can wrap
cells further. JSON/CSV values are not truncated.

Preview never writes a contract or mutates source data. It buffers at most the
requested number of rows, not the entire dataset; individual providers may
internally buffer data (for example, Parquet row groups). Cancellation is passed
to opening/async reads and checked between synchronous reads. Synchronous driver
calls cannot always be interrupted mid-call. Readers and their owned resources
are disposed on completion, failure, cancellation, and early limit termination.
