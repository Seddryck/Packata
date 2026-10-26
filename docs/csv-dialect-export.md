---
title: CSV dialect export
nav_order: 4
nav_exclude: false
permalink: /csv-dialect-export/
parent: Guides
---

# Export a CSV dialect descriptor

When you share a CSV file with another tool, it needs to know how to parse the
file: which delimiter separates fields, whether a header is present, and how
values are quoted. Use `dialect export` to turn those settings from a contract
into a standalone JSON descriptor.

The export reads settings already declared in the contract. It does not sample
the CSV to discover them. If the settings are missing or uncertain, start with
[schema inference]({{ '/guides/data/' | relative_url }}#infer-a-schema-from-the-data)
and review the inferred dialect first.

## Find the endpoint to export

Download the sample from the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }})
and inspect its contract:

```console
packata contract inspect datapackage.json --output-format json
```

Find the endpoint ID in the result. For this sample it is `customers-endpoint`.
Data Package resources normally map to `<resource-name>-endpoint`; ODCS servers
use their server ID.

Choose the endpoint whose parsing settings you want to share. A dataset can have
several representations, such as comma-separated and semicolon-separated files,
so the command requires `--endpoint` even when there is only one endpoint.

## Print or save the descriptor

Export the sample's settings to the terminal:

```console
packata dialect export datapackage.json --endpoint customers-endpoint
```

The result is JSON with a comma delimiter, a header enabled, and the sample's
newline setting. The standalone descriptor follows the Data Package v2 Table
Dialect profile and does not need a surrounding Data Package document.

To save it for another tool, choose an output path:

```console
packata dialect export datapackage.json --endpoint customers-endpoint --output customers.dialect.json
```

Diagnostics go to stderr, keeping them separate from the descriptor. Saving uses
an atomic write, and an existing file is protected. To deliberately replace that
output after changing the contract, run:

```console
packata dialect export datapackage.json --endpoint customers-endpoint --output customers.dialect.json --overwrite
```

The source contract can never be the output destination. A failed export leaves
existing output files untouched.

The command accepts local files, HTTP(S) descriptors, and local or remote ZIP, TAR, TAR.GZ and TAR.BR
packages. It opens only the descriptor, not the CSV or a database connection.
See the [dialect export reference]({{ '/cli/' | relative_url }}#export-a-csv-dialect)
for all options.

## Check the result and diagnostics

A successful export, including one with warnings, exits with code `0`. Endpoint
or dialect errors and invalid documents return `1`; invalid invocation returns
`2`; I/O failures return `3`; cancellation returns `130`.

Warnings identify settings that were left out or need attention from the receiving
tool. An unknown non-null option or an unsupported non-default extension, such as
`headerRepeat: false` or a custom dialect profile, is omitted with a warning.
Errors prevent an export from being produced.

The selected ID must identify exactly one file or object-storage path endpoint.
Packata rejects missing or duplicate IDs. It recognizes CSV using the explicit
format, then the media type, then the path extension. Supported forms include
`csv`, `csv.gz`, `csv.gzip`, `text/csv` and `.csv` paths, with optional gzip
compression. An unsupported explicit format is an error even if the filename
ends in `.csv`.

## Understand the exported settings

The descriptor can retain the delimiter, line terminator, quote and escape
characters, double quoting, header flag, header rows and joining, comment
character and rows, null sequence, and initial-space handling.

Missing settings use the dialect model's defaults: comma, CRLF, double quotes
and a first-row header. Defaults are written explicitly, while optional null
settings are omitted. For a headerless file, the export writes empty header rows
instead of the implicit `[1]` default.

Contradictory header rows, non-positive row numbers, invalid option types and
conflicting escape or quote strategies cause errors. Character settings accept
.NET characters or strings; row lists accept integer sequences; Boolean settings
require actual Boolean values.

A null quote character is exported as an empty string because the profile does
not allow JSON null for that property. Packata warns you to check how the
receiving tool handles disabled quoting. An empty quote character is accepted
when re-exporting.

The JSON uses `$schema` and camelCase property names. It leaves out Packata's
`type` discriminator and `headerRepeat` extension, non-CSV settings, the record
schema, endpoint location, encoding and compression. Share a full resource
descriptor if the receiving tool also needs those details. Check that tool's
supported settings: exporting a property does not establish that every reader,
including PocketCsvReader, applies it.

## Export from .NET

Use `Packata.DataPackage.Export.CsvDialectExporter` with a shared contract mapped
from Data Package or ODCS. This example creates two CSV endpoints and exports
the semicolon-separated representation:

```csharp
using Packata.Core.Contracts;
using Packata.DataPackage.Export;

var endpoints = new DataEndpoint[]
{
    new("customers-comma", null, EndpointKind.File, null,
        new PathLocation(["customers.csv"]),
        new DataFormat("csv", Options: new Dictionary<string, object?>
        {
            ["delimiter"] = ",", ["lineTerminator"] = "\r\n"
        })),
    new("customers-semicolon", null, EndpointKind.File, null,
        new PathLocation(["customers-eu.csv"]),
        new DataFormat("csv", Options: new Dictionary<string, object?>
        {
            ["delimiter"] = ";", ["lineTerminator"] = "\n"
        }))
};
var asset = new DataAsset("customers", "Customers", null, null, AssetKind.Table,
    Schema: null, EndpointBindings:
    [new("customers-comma"), new("customers-semicolon")]);
var contract = new DataContract(new("customers"), new(), [asset], endpoints, new());

var result = new CsvDialectExporter().Export(contract, "customers-semicolon");
var json = result
    .ReportDiagnostics(diagnostic => Console.Error.WriteLine(diagnostic.Message))
    .RequireValue();
File.WriteAllText("customers.dialect.json", json);
```

The resulting file has a semicolon delimiter and a newline terminator. Neither
CSV is opened.

If your application needs a descriptor object instead of JSON, call
`CreateDescriptor(contract, endpointId)`. It returns a
`MappingResult<TableDelimitedDialect>` with the same diagnostic behavior.
The exporter reuses Data Package's `TableDialect` and `TableDelimitedDialect`
types; Core remains independent of those native types.
