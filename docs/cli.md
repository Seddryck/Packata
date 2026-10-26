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

Most reporting commands can save their result with `--output`. This includes validation,
inspection, both kinds of comparison, preview, configuration list/get and connector list/show.
The command finishes rendering before it writes the file, refuses to replace an existing file,
and omits ANSI styling from saved output. Diagnostics still go to stderr and remain included
in JSON results where documented.

Inference handles output differently. With `schema infer --output`, the saved file is an
enriched contract in the source standard, while the interactive report still goes to stdout.
There is no separate output-format option. To update the source itself, choose `--write`;
it cannot be combined with `--output`.

Use the option names shown in this reference. The current parser can ignore unrecognized
tokens, so a successful exit alone does not prove that an unsupported flag took effect.
In particular, `--output` always names a destination file. Use `--output-format` to select
a representation; export also accepts its short alias, `-f`.

For diff and compare, `--input-format` selects the source standard for both documents.
Set `--baseline-format` or `--candidate-format` when one input needs a different choice.
An explicit `auto` on either input restores automatic detection for that document.

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

This command writes the parsing settings of one CSV endpoint as a standalone Data Package v2
Table Dialect descriptor. Use contract inspection to find the endpoint's exact ID, then pass
that ID with `--endpoint`. Export reads contract metadata without opening the CSV data.

By default, the JSON goes to stdout and diagnostics go to stderr. Use `--output` to save it
to a file. Replacing an existing output requires `--overwrite`; the source contract is always
protected. The [CSV dialect guide]({{ '/csv-dialect-export/' | relative_url }}) explains the
library API, supported settings, defaults and diagnostics, with a two-endpoint example.

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

Use this command to check an ODCS 3.2.0 contract written in YAML or a Data Package v2
descriptor written in JSON or YAML. Packata identifies the standard from the document's
contents. If you want to select the standard yourself, use the `--input-format` option
shown in the table above.

You can read the descriptor from a local file or an HTTP(S) address, either directly
or inside a ZIP, TAR, TAR.GZ or TAR.BR archive. Validation checks the contract document only; it does not
open the datasets referenced by that contract.

For any supported archive, include exactly one descriptor with one of the following filenames.
The descriptor can be in a subdirectory.

```text
datapackage.json
datapackage.yaml
datapackage.yml
contract.yaml
contract.yml
```

Archive suffixes are matched case-insensitively from the URL path; query strings
and fragments do not affect detection. All four formats have the same command
coverage: validation, inspection, diff, compatibility comparison, JSON Schema export
and dialect export. Preview and inference require an unpacked descriptor.
TAR archives use the storage layer's default materialization limits described in
the [storage guide]({{ '/guides/readers-storage/' | relative_url }}#read-a-tar-package-in-net).

```console
packata contract validate package.tar.gz --output-format json
packata contract inspect https://example.test/package.tar.br --output-format json
packata contract diff baseline.zip candidate.tar --output-format json
```

Packata checks Data Package descriptors against the library's descriptor rules.
For ODCS contracts, it checks YAML syntax and semantic rules, including the required
identity, version and status, the supported API version, and the document kind.

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

Use inspection to explore the canonical model produced from a contract. It accepts the same
sources and input-format choices as validation. Text output shows identity, metadata, assets,
schema fields and constraints, endpoint bindings, locations, formats and governance as a tree.
Mapping warnings appear on stderr.

By default, you see every asset. To focus on one, pass its exact identifier with `--asset`.
The result then includes only that asset and its referenced endpoints. An unknown or ambiguous
identifier fails instead of selecting an asset for you.

Use JSON output when another program needs the result. It returns one object containing
the source identifier, mapped contract and diagnostics. The `schemaVersion: 1` value identifies
the response format.

Within the contract, identity, metadata and governance accompany the assets, endpoints and
extensions. Each asset contains its schema fields, constraints and endpoint bindings.

Property names use camelCase, and enum values are strings. Diagnostics use the same four
fields as validation. Credentials and connection URLs are redacted throughout the result,
including nested extension metadata.

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

The command loads, detects, validates and maps each input independently. You can supply local
files, HTTP(S) addresses, or local and remote ZIP, TAR, TAR.GZ and TAR.BR archives, using the same descriptor loader
as inspection.

Comparison reads the descriptors only. It does not open referenced datasets, connect to
databases, acquire or configure connectors, or write either input.

Both same-standard and cross-standard diffs use the shared `DataContract.Diff` API.
They compare the canonical model, including preserved extension metadata, without adding
CLI-specific matching or normalization. Anything outside that mapping is outside the diff.

For ODCS, the mapper populates SLA and lifecycle commitments and typed value domains.
It does not turn native quality rules into canonical executable rules. Use `contract compare`
when you need the separate compatibility analysis and its source-coverage checks.

Unsupported source formats, values that cannot be snapshotted, duplicate semantic identifiers
and unknown library change kinds fail explicitly. They do not produce an empty, successful diff.
For example, a custom object stored in extension metadata may not be a supported snapshot value.

Text output begins with both source names and the baseline-to-candidate direction. It then
shows each change's operation, asset, semantic path, concrete kind and values. If there are
no changes, it prints `No differences detected.`

The CLI preserves the library's ordering. Text diagnostics go to stderr, and redirected text
and JSON contain no ANSI styling.

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

Each change identifies its `kind`, `operation`, `asset` and semantic `path`.
The operation is added, removed or modified. The table below lists the extra fields emitted
for each kind; fields belonging to other kinds are absent.

Applicable values preserve explicit JSON nulls. A present `"previous": null` therefore means
something different from an absent `previous` field.

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

Payload records use camelCase property names and string enum values. Extensions remain objects
keyed by namespace, with each namespace's key/value map preserved. Dictionary keys retain their
original casing, and no CLR type or assembly metadata is added.

Changes follow the library's ordering: ordinal path first, then concrete change kind.
The README's comparison section explains identifier matching, escaped paths, complete subtrees
and immutable snapshots.

A diagnostic identifies which input caused it: baseline, candidate or comparison. It also
carries `source`, `code`, `severity`, `sourcePath` and `message`. Successful JSON includes
warnings in its `diagnostics` array; in quiet mode, those warnings go to stderr instead.

Sources, diagnostic text, paths, descriptions and nested payloads are checked for credentials
and connection strings, which are redacted in both output representations. On failure,
diagnostics go to stderr and the command emits no successful or partial diff object.

A completed diff returns `0` even when it finds changes. Use `hasChanges` and `changes`
to decide what your automation should do with that result.

An invalid or unsupported contract or comparison returns `1`. Invalid invocation or option
values return `2`, an I/O failure returns `3`, and cancellation returns `130`. This command
does not apply compatibility gates, filters or rename heuristics, or generate patches.

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

Give the previous contract as the baseline and the current contract as the candidate, just
as you would for `contract diff`. The analysis asks whether consumers built against the
baseline can safely consume candidate data.

Only same-standard ODCS 3.2.0 or Data Package v2 comparisons are supported. The loader accepts
local and HTTP(S) descriptors, including ZIP, TAR, TAR.GZ and TAR.BR packages, but does not open referenced data, databases or connectors.
It detects each input's standard independently unless you select a shared input format or
override one input's format.

Use `--output-format json -o compatibility.json` to save a report. The command protects
existing files, including the source descriptors. Quiet mode suppresses file creation as well
as stdout.

A completed report is saved even when its findings fail the selected gate. Invalid documents
and failed analyses use the structured failure envelopes described below.

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

When a finding has a typed `change`, its payload uses the same kind, operation, asset, path
and values documented for `contract diff`. Several findings can refer to the same change.
Coverage findings, and model details not represented by the differ, use `change: null`.
An unsupported change in the raw source may therefore be absent from `hasChanges`.

The backend currently has no line/column mapping, so `sourceLocation` is explicitly null.
Coverage diagnostic paths point into the source descriptor, while typed change paths identify
canonical assets. Sources, diagnostics and nested typed values are redacted.

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

If input is invalid, loading fails, analysis fails or the command is cancelled, JSON uses
the same envelope with `completed: false`, `outcome: failed` and `isCompatible: false`.
Both `hasChanges` and `gatePassed` are null. Diagnostics explain the failure, and findings
may be partial.

An invalid invocation can also return a JSON failure envelope when JSON output was requested
for stdout. The sources are then null, and the usage error also goes to stderr. The accepted
spellings are `--output-format json`, `--output-format=json` and `--output-format:json`.
Destination options accept the same space, equals and colon forms with `--output` or `-o`.

Invocation errors never create output files. Options after `--` do not select presentation,
and an invalid output-format value cannot itself request JSON. Quiet mode, including an
explicit true value, suppresses every envelope; `--quiet=false` retains normal reporting.
Otherwise, JSON diagnostics stay inside the envelope and ordinary logs do not mix with stdout.

| Exit code | Meaning |
| --- | --- |
| 0 | Completed analysis passes the selected gate |
| 1 | Invalid/malformed contract or descriptor package |
| 2 | Invalid invocation/options |
| 3 | Source or output I/O/access failure |
| 4 | Completed analysis contains a classification selected by --fail-on |
| 5 | Analysis/mapping cannot complete, including unsupported cross-standard comparisons |
| 130 | Cancellation |

By default, the gate fails on breaking, potentially breaking or unknown findings. Choose
`--fail-on none` when you want a report without classification-based gating. This changes
the exit-code gate, not the classifications, and never turns unknown coverage into compatibility.

Disabling the gate does not waive invocation, invalid-input, I/O, analysis or cancellation
failures. JSON already supplies the classifications and explanations needed by #572;
GitHub annotations, validation line/column mapping and MCP registration remain separate work.

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

Export one asset from an ODCS or Data Package v2 descriptor. You can use the same local,
HTTP(S) and ZIP, TAR, TAR.GZ and TAR.BR sources as inspection. Select the asset by its exact canonical identifier;
a missing or ambiguous identifier fails.

The required output format is JSON Schema. Select it with `--output-format` or its short
alias, `-f`. The complete syntax and accepted values are listed above.

The result is a JSON Schema Draft 2020-12 object describing one dataset record. By default,
only the generated JSON goes to stdout, while diagnostics go to stderr. To save it instead,
choose a new UTF-8 file with `--output` or `-o`. Existing files, including the source contract,
cannot be replaced.

Export reads only the descriptor. It does not access referenced data or install connectors,
and it uses the validation command's exit codes.

The converter supports strings, booleans, integers, numbers and decimals, dates, timestamps
and datetimes, times, arrays and objects. It uses JSON Schema formats for temporal values
and recursively converts nested objects and array items.

Representable constraints are retained. These include string lengths and patterns, numeric
bounds, enums, array size and uniqueness, and object size and required properties.

Requiredness and nullability are independent. A required field is added to its parent's
`required` list. A nullable field allows null alongside its normal type. When nullability
is omitted, it defaults to false.

Conversion fails for unknown types, including maps and vectors, invalid constraints, temporal
range constraints, and dataset-wide uniqueness or primary-key constraints. A schema for one
record cannot enforce uniqueness across records.

The export does not emit custom extension keywords. Physical storage details, relationships,
quality rules and governance metadata are outside this record-schema representation.

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

Preview reads an asset from an ODCS YAML or Data Package v2 JSON/YAML descriptor,
or a local/HTTP(S) ZIP containing exactly one supported descriptor. CSV resources
inside a ZIP are read directly from archive entries without extracting the package.
Their paths resolve relative to the descriptor inside the archive, including when
that descriptor is in a subdirectory:

```console
packata data preview dataset.zip --asset customers
```

For example, `datapackage.json` can reference `data/customers.csv` in the same ZIP.
Select the asset explicitly with `--asset`. Its canonical binding supplies the file paths,
physical table names, schema and format or dialect settings. Missing or ambiguous assets and
bindings fail rather than being selected automatically. Row limits, column selection and
all output formats also apply to ZIP previews.


Relative file paths and database files resolve against the descriptor's directory, so the
working directory does not change which data is read. Preview registers readers for delimited
and inline data, Excel, Parquet and databases. Database reads use the shared connector service
and can download a missing connector when the configured policy permits it.

Preview returns at most 20 data rows by default, excluding headers. Set `--limit` to any
positive 32-bit integer to change that bound. Reading stops at the limit or the end of the data.

To select or reorder columns, pass their exact names as a comma-separated `--columns` value.
Omit it to return all columns. Invalid, duplicate or ambiguous names fail.

Preview displays a table by default. Use `--output-format` to choose JSON or CSV, and
`--output` or `-o` to save the result to a new file instead of stdout.

Diagnostics go to stderr. JSON and CSV stdout contain only the requested data representation,
and redirected output has no ANSI styling.

JSON uses the stable shape `{"schemaVersion":1,"columns":["id","name"],"rows":[[1,"Alice"]]}`.
Each row is an array whose positions match the column list. This preserves JSON nulls and
avoids object-key collisions. Empty results still include column names when the reader or
declared schema supplies them.

CSV includes a header and quotes delimiters, quotation marks and line breaks. Nulls become
empty fields. Values use invariant culture, dates use round-trip format and binary values
are base64.

The table view displays null as `(null)`, flattens line breaks and truncates cells beyond
80 characters; terminal layout may wrap cells further. JSON and CSV values are not truncated.

Preview does not write the contract or change source data. It buffers at most the requested
rows, although a provider may buffer internally, such as a Parquet reader loading a row group.

Cancellation is passed to opening and asynchronous reads, and checked between synchronous
reads. A synchronous driver call cannot always be interrupted while it is running. Readers
and the resources they own are disposed after completion, failure, cancellation or early stopping.
