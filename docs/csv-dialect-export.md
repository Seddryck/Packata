---
title: CSV dialect export
nav_order: 4
nav_exclude: false
permalink: /csv-dialect-export/
parent: Guides
---

# Export a CSV dialect descriptor

Use `Packata.DataPackage.Export.CsvDialectExporter` to export the parsing settings of one
canonical CSV endpoint. Choose the endpoint explicitly; no CSV data is opened or sampled,
so the stored settings must already describe the file.

The exporter reuses `TableDialect` and `TableDelimitedDialect` from Data Package.
Core remains independent of those native types.

The standalone JSON targets the [Data Package v2 Table Dialect profile](https://datapackage.org/profiles/2.0/tabledialect.json),
the successor to CSV Dialect. No Data Package wrapper is required. This is a
descriptor export, not a claim that every Table Dialect property is supported by
PocketCsvReader or another reader; consumers must check their supported settings.

## .NET library

An asset can have multiple CSV representations with different parsing settings:

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

You can pass a contract mapped from either Data Package or ODCS directly to the exporter.
If you need the descriptor model rather than JSON, call `CreateDescriptor(contract, endpointId)`
to obtain a `MappingResult<TableDelimitedDialect>`.

Both operations report diagnostics. Errors prevent output; warnings identify settings that
cannot be represented.

## CLI

```console
packata contract inspect datapackage.json --output-format json
packata dialect export datapackage.json --endpoint customers-endpoint
packata dialect export datapackage.json --endpoint customers-endpoint --output customers.dialect.json
packata dialect export contract.yaml --endpoint local --output local.dialect.json --overwrite
```

Find the exact endpoint ID with `contract inspect`. Data Package resources normally map to
`<resource-name>-endpoint`, while ODCS servers use their server ID. You must pass `--endpoint`
even when the contract has only one endpoint.

The command accepts the same local, HTTP(S) and ZIP descriptors as inspection. It reads only
the descriptor, without opening the referenced resource or acquiring database connectors.

By default, descriptor JSON goes to stdout and diagnostics go to stderr. Saving to a file
uses an atomic write. Replacing an existing output requires `--overwrite`, and replacing the
source contract is always refused. A failed export does not replace output files.

Success, including warnings, returns 0. Invalid documents or endpoint/dialect errors return 1,
invocation errors return 2, I/O failures return 3, and cancellation returns 130.

## Defaults, scope, and diagnostics

The selected ID must identify exactly one file or object-storage path endpoint. Missing and
duplicate IDs are rejected.

Format selection uses the explicit format name first, then media type, then path extension.
Supported forms include `csv`, `csv.gz`, `csv.gzip`, `text/csv` and `.csv` paths, optionally
gzip-compressed. An unsupported explicit format is rejected even if the path ends in `.csv`.

The export retains delimiter, line terminator, quote and escape characters, double quoting,
header flag/rows/join, comment character/rows, null sequence and initial-space handling.
Unspecified settings use the dialect model's defaults, including comma, CRLF, double quotes
and a first-row header. Defaults are written explicitly, while optional null settings are omitted.

For headerless files, the export writes empty header rows instead of the implicit `[1]` default.
Contradictory header rows, non-positive row numbers, invalid option types and conflicting
escape/quote strategies produce errors.

A null quote character becomes an empty string in JSON, with a warning to check
the consumer's disabled-quoting behavior; the profile does not allow JSON null
for that property. An empty quote character is accepted when re-exporting.
Character-valued settings accept either .NET characters or strings, row lists
accept integer sequences, and boolean settings require actual booleans.

The exporter warns when it omits an unknown non-null option or an unsupported non-default
extension, such as `headerRepeat: false` or a custom dialect profile.

The standalone output uses `$schema` and camelCase property names. It excludes Packata's
`type` discriminator and `headerRepeat` extension, non-CSV dialect properties, record schema,
endpoint location, encoding and compression. Use a full resource descriptor when you need
those additional resource settings.