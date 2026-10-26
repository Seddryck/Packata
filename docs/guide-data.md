---
title: Read data and infer schemas
parent: Guides
nav_order: 2
nav_exclude: false
permalink: /guides/data/
---

A contract tells you what a dataset should look like. Preview lets you look at a
few actual records, while inference suggests a schema from the data it finds.
Use preview to check that you are reading the right dataset, then use inference
when you need to create or update its field definitions.

Download `datapackage.json` and `customers.csv` from the
[CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) into the same directory.
Run the following commands from that directory.

## Preview a few records

Use `data preview` to read a small selection of rows before working with a larger
dataset:

```console
packata data preview datapackage.json --asset customers --columns name,id --limit 2 --output-format csv
```

`--asset customers` selects the dataset described in the contract. `--columns`
selects its fields and sets their output order, and `--limit 2` reads at most two
records. Here the result is:

```csv
name,id
Alice,1
Bob,2
```

Omit `--columns` to include every field. Omit `--output-format csv` to view a table
in the terminal, or choose `json` when another program needs the result. Preview
does not change the data or the contract.

The row limit bounds the preview output. Non-seekable Excel/Parquet inputs may first
be downloaded completely under `--staging-maximum-bytes` (256 MiB by default), and
Parquet preview currently materializes all row groups. See [staging and parser costs]({{ '/guides/readers-storage/' | relative_url }}#stage-excel-and-parquet-resources).
A successful preview shows that those rows can be read; it does not certify that
all rows satisfy every contract rule. The
[preview reference]({{ '/cli/' | relative_url }}#preview-bound-data) lists all
options and supported sources.

## Preview category labels

Both ODCS enums and DPv2 inline categories can give stored codes human-readable
labels. Save this CSV as `directions.csv` beside either descriptor below:

```csv
direction,rating
S,1
N,2
X,3
```

The last row deliberately demonstrates unmatched-code fallback; preview does
not certify that rows satisfy the contract's allowed values.

### Data Package categories

Save this complete descriptor as `datapackage.json`:

```json
{
  "resources": [{
    "name": "directions",
    "path": "directions.csv",
    "format": "csv",
    "dialect": {"header": true},
    "schema": {"fields": [
      {"name": "direction", "type": "string", "categories": [
        {"value": "S", "label": "South"}, {"value": "N"}
      ]},
      {"name": "rating", "type": "integer", "categories": [
        {"value": 1, "label": "Low"}, {"value": 2}
      ]}
    ]}
  }]
}
```

Run both commands:

```console
packata data preview datapackage.json --asset directions --output-format json
packata data preview datapackage.json --asset directions --output-format json --decode-categories
```

Default output:

```json
{"schemaVersion":1,"columns":["direction","rating"],"rows":[["S",1],["N",2],["X",3]]}
```

With decoding enabled:

```json
{"schemaVersion":1,"columns":["direction","rating"],"rows":[["South","Low"],["N","2"],["X","3"]]}
```

### ODCS enumerations

Save this equivalent complete descriptor as `contract.yaml`:

```yaml
apiVersion: v3.2.0
kind: DataContract
id: directions
version: 1.0.0
status: draft
schema:
  - name: directions
    physicalType: file
    properties:
      - name: direction
        logicalType: string
        enum:
          - value: S
            label: South
          - value: N
      - name: rating
        logicalType: integer
        enum:
          - value: 1
            label: Low
          - value: 2
servers:
  - server: local
    type: local
    path: directions.csv
    format: csv
```

```console
packata data preview contract.yaml --asset directions --output-format json
packata data preview contract.yaml --asset directions --output-format json --decode-categories
```

The first command returns `[["S",1],["N",2],["X",3]]` in `rows`, and the second
returns `[["South","Low"],["N","2"],["X","3"]]`, exactly as in the DPv2 example.
Whitespace in JSON output can differ.

### Native matching and returned types

Decoding is disabled by default and uses PocketCsvReader's native `WithSequence`
support for CSV/TSV/PSV. Other engines, including Excel, Parquet, databases and
inline data, return an explicit unsupported-engine diagnostic. Category table
references are not expanded.

**Cells to decode must contain unquoted, unescaped code text.** PocketCsvReader
skips native sequence replacement for fields it marks as quoted or escaped,
even when their parsed text matches a code. Codes requiring CSV quoting or
escaping therefore cannot be decoded. This concerns CSV cells, not the normal
JSON/YAML quoting used in descriptors.

Matching is exact and case-sensitive. Write `S` to obtain `South`; quoted `"S"` remains `S`,
and integer spelling `01` remains `01`, rather than matching the sequence for `1`.
Entries without labels and unmatched codes keep their text. Empty labels stay
empty, nulls stay null, and decoding does not add any new null markers.

Any column with at least one label returns strings for both labels and fallback
codes. Its .NET `GetFieldType` is `typeof(string)` and `GetDataTypeName` is `string`.
Columns without labels retain their usual types. PocketCsvReader does not implement
`GetSchemaTable()`. The canonical field type and allowed codes remain unchanged;
use a default reader for row validation. No descriptor or CSV is edited.

The [.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}#read-category-labels-from-either-standard)
shows canonical mapping and reader configuration for both descriptors.

<a id="preview-a-zipped-data-package"></a>

## Preview an archived data package

You can distribute the descriptor and CSV files together in a ZIP, TAR, TAR.GZ or
TAR.BR archive and preview the selected resource without manually extracting the package:

```text
dataset.zip
|-- datapackage.json
`-- data/
    |-- customers.csv
    `-- orders.csv
```

In `datapackage.json`, the `customers` resource uses `"path": "data/customers.csv"`.
Read it with the same preview options:

```console
packata data preview dataset.zip --asset customers --columns name,id --limit 2 --output-format csv
packata data preview dataset.tar --asset customers --limit 2 --output-format json
packata data preview dataset.tar.gz --asset customers --columns name,id --limit 2 --output-format csv
packata data preview https://example.test/dataset.tar.br --asset customers --limit 2
```

Only the selected resource is parsed. The archive must contain exactly one supported
descriptor; CSV paths resolve relative to its directory inside the archive.
Local files and accessible HTTP(S) URLs are supported for all four archive formats. Missing descriptors
or referenced entries produce an error. Preview leaves the archive unchanged.
TAR entries are materialized once using the storage layer's memory and temporary-file
limits, and the container remains open until preview finishes. Resource paths are
resolved relative to the descriptor, including `../` paths that stay inside the archive.
Streams and temporary storage are released after success, failure or cancellation.
See the [storage guide]({{ '/guides/readers-storage/' | relative_url }}#read-a-tar-package-in-net)
for limits and patched Brotli runtime requirements.
Preview also supports Excel and Parquet members through the shared bounded staging
adapter when needed. Schema inference accepts these archive formats and HTTP/HTTPS descriptors and
data files, including CSV-family, Excel and Parquet resources. See [inference transport,
staging limits and output restrictions]({{ '/cli-inference/' | relative_url }}).

## Infer a schema from the data

Use `schema infer` when a contract has missing or outdated field definitions.
For a delimited file, Packata samples records and reports the types and CSV
settings it discovers:

```console
packata schema infer datapackage.json --asset customers --header present --maximum-records 100
```

`--header present` tells Packata that the first row contains field names.
`--maximum-records 100` limits the sample to 100 data records. With the Quickstart
sample, the report identifies the `id` and `name` fields and their inferred types.
The descriptor is left unchanged.

Sampling gives you a starting point for a schema. Review the diagnostics and try a
larger sample if rare values could affect the inferred types. You can also set a
byte limit, choose the culture used to interpret values, or control dialect
inference. See the [inference reference]({{ '/cli-inference/' | relative_url }})
for the complete options.

## Save and review the inferred contract

To keep the result, write an enriched copy to a new file:

```console
packata schema infer datapackage.json --asset customers --output enriched.json
```

The new file keeps the source document's standard and incorporates the inferred
schema and endpoint settings. The original `datapackage.json` is unchanged. Choose
a destination that does not already exist; inference protects existing files.

Review the new descriptor and any preservation warnings before adopting it.
Rewriting JSON or YAML can change formatting and comments, and some source
metadata cannot be preserved. The
[contract guide]({{ '/guides/contracts/' | relative_url }}#find-changes-with-a-diff)
shows how to review the differences between the original and enriched versions.

If you deliberately want to replace the source, use `--write` instead of
`--output`. It replaces the descriptor without an overwrite prompt. The two
options cannot be combined.

## Infer a database schema

For an asset bound to a database, the same `schema infer` command discovers table
metadata through the database connection. It does not sample CSV records, so CSV
header, culture and sampling options do not affect that discovery.

Start with the [database connection guide]({{ '/database-integration/' | relative_url }})
to configure the endpoint and connector. Review discovery diagnostics: the
returned metadata can be incomplete.

## Infer a CSV schema in .NET

In an application, you can infer directly from a CSV stream without creating a
contract first:

```csharp
using Packata.ResourceReaders;

using var stream = File.OpenRead("customers.csv");
var result = new CsvDataContractInference().Infer(stream, "customers.csv");
foreach (var diagnostic in result.Diagnostics)
    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
var inferred = result.RequireContract();
```

This returns a contract in Packata's shared model. It does not write a descriptor
file. Use `CsvDataContractInferenceOptions` to configure sampling, culture, header
and dialect detection, or to choose the generated contract, asset and endpoint
metadata.

For database discovery, use `DatabaseDataContractInference` with
`DatabaseDataContractInferenceOptions` and configure the application's connection
service. As with the CLI, check its diagnostics before accepting the result.

To read records through `IDataReader`, follow the
[.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}).

## Choose a supported format

Inference currently supports CSV-family file bindings and database bindings.
Preview can read additional formats, but the ability to read a format does not
mean Packata can infer its schema. The
[storage and reader guide]({{ '/guides/readers-storage/' | relative_url }})
explains which formats are available in the CLI and how to add readers in .NET.
