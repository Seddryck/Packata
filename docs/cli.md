---
title: Command-line interface
nav_order: 3
nav_exclude: false
permalink: /cli/
---

# Packata command-line interface

Install the .NET tool with `dotnet tool install --global Packata-cli`. Run
`packata --help` or `packata --version` to discover commands and the installed version.
The existing `packata infer` command remains available.

## Export a CSV dialect

```console
packata dialect export datapackage.json --endpoint customers-endpoint
packata dialect export contract.yaml --endpoint local --output customers.dialect.json
```

Export a selected CSV endpoint as standalone Data Package v2 Table Dialect JSON.
Use `contract inspect` to find the endpoint ID. `--endpoint` is required; without
`--output` the descriptor goes to stdout. Diagnostics go to stderr. Replacing an
existing output file requires `--overwrite`; the source contract is protected.
Export reads contract metadata without opening CSV data. See
[CSV dialect export]({{ '/csv-dialect-export/' | relative_url }}) for the library API, supported
settings, defaults, diagnostics, and an example with two endpoint dialects.

## Validate a contract

```console
packata contract validate contract.yaml
packata contract validate datapackage.json --output json
packata contract validate descriptor --format odcs
```

Validation accepts ODCS v3.2.0 YAML and Data Package v2 JSON/YAML. Detection examines
the document root; `--format auto|odcs|datapackage` overrides standard detection.
Local files, HTTP(S) descriptors, and local/HTTP(S) ZIP packages are supported.
A ZIP must contain exactly one descriptor named `datapackage.json`,
`datapackage.yaml`, `datapackage.yml`, `contract.yaml`, or `contract.yml`, including
inside a subdirectory. Validation never opens referenced data resources.

Data Package validation uses the library's descriptor validator. ODCS validation
uses the library's YAML/semantic validation plus checks for required identity,
version, status, supported API version, and document kind.

`--output text` is the default. Text diagnostics go to stderr. `--output json`
writes a single object to stdout with `schemaVersion: 1`, `source`, `valid`, and
`diagnostics`. Each diagnostic has `code`, `severity`, `sourcePath`, and `message`.
Diagnostics do not include raw parser snippets or stack traces; connection secrets
are redacted. Redirected output has no ANSI styling.

Exit codes for these new commands: `0` success, `1` invalid document or operation,
`2` invocation error, `3` I/O failure, and `130` cancellation. These do not change
the existing `infer` command's exit-code contract.

## Inspect the canonical contract

```console
packata contract inspect contract.yaml
packata contract inspect datapackage.json --asset customers --output json
```

Inspection uses the same source loading and `--format` options as validation.
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

## Compare two contract versions

```console
packata contract diff sales-v1.odcs.yaml sales-v2.odcs.yaml
packata contract diff old/datapackage.json new/datapackage.json --output json
packata contract diff old/descriptor new/descriptor --baseline-format odcs --candidate-format datapackage
packata contract diff old.zip https://example.test/new.zip --quiet
```

The **baseline is previous** and the **candidate is current**. An addition is present
only in the candidate, a removal only in the baseline, and a modification retains
baseline-to-candidate values. Differences are factual data; this command does not
apply a compatibility policy or decide whether a change is breaking.

| Option | Values | Default |
| --- | --- | --- |
| `--baseline-format` | `auto`, `odcs`, `datapackage` | `auto` |
| `--candidate-format` | `auto`, `odcs`, `datapackage` | `auto` |
| `--output` | `text`, `json` | `text` |
| `--quiet` | Suppress all result output, including JSON; diagnostics remain on stderr | off |

Each input is independently detected, validated, and mapped using the inspection
command's descriptor loader. Local, HTTP(S), and local/remote ZIP sources are
supported. Referenced datasets are never opened, databases are never connected,
and connectors are never acquired or configured. The command does not write either input.

ODCS-to-ODCS and Data Package v2-to-Data Package v2 comparisons use the canonical
library's `DataContract.Diff` API. Cross-standard comparisons also use that shared
model directly, including preserved extension metadata; there is no CLI-specific
matching or normalization. Differences outside the canonical mapping are not
compared. For example, the current ODCS mapper does not populate canonical SLAs or
quality rules; those change kinds are presented when returned by the library.
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

## Export a dataset schema

```console
packata export sales.odcs.yaml --format json-schema --dataset orders
packata export sales.odcs.yaml -f json-schema -d orders -o orders.schema.json
```

`export` accepts the same local, HTTP(S), and ZIP descriptors as inspection,
including ODCS and Data Package v2 documents. `--format` (`-f`) selects the target
representation and must be `json-schema`. `--dataset` (`-d`) is required and selects
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

## Preview bound data

```console
packata data preview contract.yaml --asset customers --limit 20
packata data preview datapackage.json --asset customers --limit 5 --columns id,name
packata data preview contract.yaml --asset customers --output json > customers.json
```

The source is an unpacked ODCS YAML or Data Package v2 JSON/YAML descriptor;
`--asset` is required. Paths, physical table names, schemas, and format/dialect
metadata come from its canonical binding. Relative file paths and database files
resolve against the descriptor directory, independently of the current directory.
Missing or ambiguous assets and endpoint bindings fail instead of selecting one.
The command registers delimited/inline, Excel, Parquet, and database providers.
Database connector availability follows the shared reader stack; the CLI does
not download connectors itself.

`--limit` defaults to **20**, must be a positive 32-bit integer, and excludes
headers. Reads stop at that limit or end of data. `--columns` is a comma-separated
list of unique, exact field names in output order; omitted means all columns.
An invalid name, duplicate name, or ambiguous reader column fails.

`--output table|json|csv` defaults to `table`. Here `--output` selects a
representation; it is not the file-path option used by `infer`. Use shell
redirection to save results. Diagnostics go to stderr. JSON/CSV stdout contains
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
