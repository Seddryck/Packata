---
title: Schema inference
parent: Command-line interface
grand_parent: Reference
nav_order: 1
nav_exclude: false
permalink: /cli-inference/
---

<!-- cli:schema infer -->
<!-- cli:infer -->
## schema infer / infer

```text
packata schema infer <SOURCE> --asset <ID> [OPTIONS]
packata infer <SOURCE> --asset <ID> [OPTIONS]
```

The `infer` entry point is an alias for schema inference, with the same options and behavior.
Supply a local ODCS YAML or Data Package v2 JSON/YAML descriptor. Packata detects its standard
from the contents unless you set `--input-format`, and accepts extensionless descriptors.
This loader does not accept remote descriptors or ZIP, TAR, TAR.GZ or TAR.BR archives.

Select one exact asset with exactly one endpoint binding. For CSV inference, that binding
must provide a single CSV, TSV or PSV path; an ODCS CSV endpoint must be a local file.
For a database, the shared service discovers the selected physical object's metadata and
reports diagnostics when discovery is partial.

Excel (`xls`/`xlsx`) and Parquet (`parquet`/`pqt`) bindings also use one file path.
Parquet extracts declared footer metadata without sampling rows; Excel profiles one
rectangular worksheet and reports sampling uncertainty. ODCS file endpoints must be
local; Data Package resources use the existing resource resolver. Explicit format takes
precedence over a recognized media type and path extension. Streams must be seekable.

| Option | Values / default | Meaning |
| --- | --- | --- |
| `--input-format <FORMAT>` | `auto`, `odcs`, `datapackage`; `auto` | Source-standard selection; descriptor must still be local |
| `--asset <ID>` | Required | Exact canonical asset identifier |
| `--output <PATH>`, `-o <PATH>` | Omitted | Write an enriched copy to a **new** file; missing parent directories are created |
| `--write` | Off | Atomically replace the source with its enriched form; mutually exclusive with `--output` |
| `--maximum-records <COUNT>` | Positive integer; `1000` | Maximum sampled CSV/Excel data records; ignored for databases, rejected for Parquet |
| `--maximum-bytes <COUNT>` | Positive integer; CSV unlimited, Excel 64 MiB when omitted | CSV sample byte limit or complete Excel workbook byte limit; ignored for databases, rejected for Parquet |
| `--culture <NAME>` | Invariant culture when omitted/empty | Culture for inferred CSV values, e.g. `fr-FR`; ignored for databases |
| `--header <MODE>` | `auto`, `present`, `absent`; `auto` | CSV/Excel header mode; auto honors a declared header hint; ignored for databases, rejected for Parquet |
| `--sheet-name <NAME>` | Omitted | Excel worksheet name, overriding endpoint selection; mutually exclusive with sheet number |
| `--sheet-number <NUMBER>` | Positive integer; endpoint hint or first worksheet | Excel worksheet number starting at one; mutually exclusive with sheet name |
| `--no-dialect-inference` | Off | Use contract dialect hints without inferring a CSV dialect; ignored for databases |
| `--connector <SCHEME=ID>` | Repeatable; persisted preferences otherwise | Invocation-only connector preference |
| `--connection-env <ENDPOINT=VARIABLE>` | Repeatable; no override by default | Runtime database URL from a named environment variable; not persisted |

CSV sampling and dialect options do not affect database discovery, but their supplied values
still have to pass syntax and value validation.
Explicit culture/dialect switches are rejected for Excel. Explicit CSV sampling,
header, culture and dialect switches are rejected for Parquet. Excel sheet switches
are rejected for other file formats. Omitting these options permits normal dispatch.

Inference has no output-format switch because an enriched file keeps the source representation.
It also has no `--overwrite` option. Use `--output` or its alias, `-o`, to create a new file.
See [database override precedence]({{ '/cli-database/' | relative_url }}#invocation-overrides)
for runtime connection settings.

```console
packata schema infer datapackage.json --asset customers --header present --maximum-records 100
packata infer datapackage.json --asset customers --maximum-bytes 1048576 --culture en-US
packata schema infer datapackage.json --asset customers --no-dialect-inference --output enriched.json
packata schema infer contract.yaml --asset customers --connector postgresql=postgresql-npgsql --connection-env warehouse=WAREHOUSE_URL
packata schema infer workbook-contract.yaml --asset measurements --sheet-name Measurements --header present --write
packata schema infer parquet-contract.yaml --asset measurements --output enriched.yaml
```

## Output and mutation

Inference prints a human-readable report of its mapping and inference decisions, the selected
schema and dialect, and confidence where available. The report has no machine-readable
output contract.

Without an output option, the descriptor is left unchanged. Choose `--output` to create a
new enriched file; an existing file is refused. Choose `--write` only when you want to replace
the source itself. That update has no overwrite prompt.

An update enriches the selected resource or schema and its endpoint metadata. It does not
convert the document into another standard. Review preservation warnings before accepting it:
JSON or YAML is reserialized, comments and formatting may change, YAML anchors are not
guaranteed to round-trip, and unsupported source constructs can limit the update.

Parquet maps declared scalar types/nullability, structs and lists; unsupported maps or
legacy repeated fields produce error diagnostics. Nested schemas are display-only in
the current document updater and persistence fails before writing. Physical Parquet
facets remain in the library result and generate a warning when only scalar logical
types/nullability are written. No CSV dialect is generated for Parquet.

Excel preserves selected sheet/header/leading-row decisions in spreadsheet dialect
settings or ODCS Packata custom properties. Sampling never establishes requiredness;
new fields are optional and authored requiredness is preserved. Formula caches may be
stale, merged cells are not filled forward, and ambiguous headers/mixed types produce
diagnostics. Named tables, arbitrary ranges and multi-row headers are unsupported.
See [file schema inference]({{ '/file-schema-inference/' | relative_url }}) for library examples, mappings,
stream ownership, resource limits and further limitations.

Runtime connection overrides are not saved in the enriched contract. Relative output paths
resolve from the working directory, while data paths resolve from the contract's resource
context. File-inference errors may include source-specific text, so redact sensitive details
before sharing logs.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Inference completed |
| `1` | Inference, input, write or database operation failed |
| `2` | Inference caught cancellation; also CLI invocation/settings errors |
| `130` | Framework-level cancellation when handled before/around command execution |

These are the legacy inference semantics, not the `0/1/2/3/130` contract of validation/inspection/export/preview/diff. CSV inference is synchronous inside its command path and may not stop mid-sample. See [read/infer task guide]({{ '/guides/data/' | relative_url }}).
