# Packata command-line interface

Install the .NET tool with `dotnet tool install --global Packata-cli`. Run
`packata --help` or `packata --version` to discover commands and the installed version.
The existing `packata infer` command remains available.

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
