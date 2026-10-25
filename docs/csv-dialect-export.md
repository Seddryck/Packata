---
title: CSV dialect export
nav_order: 6
nav_exclude: false
permalink: /csv-dialect-export/
---

# Export a CSV dialect descriptor

`Packata.DataPackage.Export.CsvDialectExporter` exports the parsing configuration
of one explicitly selected canonical CSV endpoint. It reuses `TableDialect` and
`TableDelimitedDialect`; Core does not depend on Data Package types. No CSV data
is opened or sampled, so the endpoint's settings must already describe the file.

The standalone JSON targets the [Data Package v2 Table Dialect profile](https://datapackage.org/profiles/2.0/tabledialect.json),
the successor to CSV Dialect. No Data Package wrapper is required. This is a
descriptor export, not a claim that every Table Dialect property is supported by
PocketCsvReader or another reader; consumers must check their supported settings.

## Library

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

Contracts mapped from Data Package or ODCS documents can be passed directly to
the exporter. `CreateDescriptor(contract, endpointId)` returns a
`MappingResult<TableDelimitedDialect>` when the caller needs the model instead
of JSON. Both operations return diagnostics; errors prevent output and warnings
identify settings that cannot be represented.

## CLI

```console
packata contract inspect datapackage.json --output json
packata dialect export datapackage.json --endpoint customers-endpoint
packata dialect export datapackage.json --endpoint customers-endpoint --output customers.dialect.json
packata dialect export contract.yaml --endpoint local --output local.dialect.json --overwrite
```

Use the exact endpoint ID shown by `contract inspect`. Data Package resources
normally map to `<resource-name>-endpoint`; ODCS servers use their server ID.
`--endpoint` is required even when there is only one endpoint. The command accepts
the same local, HTTP(S), and ZIP descriptors as contract inspection and does not
open the referenced resource or acquire database connectors.

Without `--output`, stdout contains only descriptor JSON. Diagnostics go to
stderr. File output uses an atomic write, requires `--overwrite` to replace an
existing file, and refuses the source contract path. Failed exports do not
replace output files. Exit codes are 0 for success (including warnings), 1 for
invalid documents or endpoint/dialect errors, 2 for invocation errors, 3 for I/O
failures, and 130 for cancellation.

## Defaults, scope, and diagnostics

Selection uses the exact endpoint ID and rejects absent or duplicate IDs. The
endpoint must be a file or object-storage path endpoint. An explicit format name
takes precedence, then media type, then path extensions: `csv`, `csv.gz`,
`csv.gzip`, `text/csv`, and `.csv` paths (optionally gzip-compressed) are supported.
Other explicit formats are rejected, even if the path ends in `.csv`.

The exporter preserves delimiter, line terminator, quote and escape characters,
double quoting, header flag/rows/join, comment character/rows, null sequence, and
initial-space handling. Unspecified settings use the existing dialect model's
defaults, including comma, CRLF, double quotes, and a first-row header. Default
values are emitted explicitly; optional null settings are omitted. Headerless
files export empty header rows instead of the model's implicit `[1]` default.
Other contradictory header rows, non-positive row numbers, invalid option types,
and conflicting escape/quote strategies produce errors.

A null quote character becomes an empty string in JSON, with a warning to check
the consumer's disabled-quoting behavior; the profile does not allow JSON null
for that property. An empty quote character is accepted when re-exporting.
Character-valued settings accept either .NET characters or strings, row lists
accept integer sequences, and boolean settings require actual booleans.

Unknown non-null options and unsupported non-default extensions (such as
`headerRepeat: false` or a custom dialect profile) produce warnings when omitted.
The standard output uses `$schema` and camel-case property names. It excludes
Packata's `type` discriminator and `headerRepeat` extension, non-CSV dialect
properties, record schema, endpoint location, encoding, and compression. A full
resource descriptor is needed to carry those additional resource settings.
