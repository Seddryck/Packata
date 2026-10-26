---
title: Configure storage and readers
parent: Guides
nav_order: 3
nav_exclude: false
permalink: /guides/readers-storage/
---

To read a dataset, Packata needs to find the data and choose a reader for its
format. Both the CLI and .NET library can read database tables as well as files.
A contract supplies the location and format settings; a database read also needs
a compatible connector or provider and connection credentials where required.

An endpoint describes a file location or database connection. An asset's binding
identifies the path or table that holds that dataset. If an asset has several
bindings, choose the one you intend to read rather than relying on a default.
Loading the contract alone does not open any of its data.

## Choose where to load the contract file

The contract file and the data it describes can live in different places. For
example, a local `contract.yaml` can describe a table in a remote PostgreSQL
database. Packata loads that file to find the table and connection settings, then
connects to the database when you preview its records.

The table below lists where the CLI can load the **contract file**. It does not
restrict the dataset to files:

| Task | Accepted descriptor locations |
| --- | --- |
| Validate, inspect, diff, assess compatibility, or export metadata | Local files, HTTP(S) URLs, or local and remote ZIP archives |
| Preview records | An unpacked local or HTTP(S) descriptor |
| Infer a schema | A local descriptor, including a file without an extension |

Packata normally detects the contract standard from its contents. If you need to
select it explicitly, use `--input-format auto`, `odcs` or `datapackage`. Selecting
a format does not expand the locations a command accepts. See the
[CLI reference]({{ '/cli/' | relative_url }}) for command-specific details.

For example, the Quickstart sample can be previewed with:

```console
packata data preview datapackage.json --asset customers --limit 2
```

This opens the CSV referenced by the `customers` asset and displays up to two rows.
The [data guide]({{ '/guides/data/' | relative_url }}) explains the output and the
additional preview options.

## Choose how to read the dataset

CLI preview includes readers for delimited files, inline data, Excel, Parquet and
databases. With a database-bound asset, `data preview` reads table records and
`schema infer` discovers table metadata. Follow the
[database connection guide]({{ '/database-integration/' | relative_url }}) for
connector setup, runtime credentials and examples of both commands.

In .NET, CSV, TSV, PSV and inline-data readers are built in. Other formats require
an additional package and registration. Fixed-width, NDJSON, key-value and web-log
readers are available in .NET but are not currently registered for CLI preview.

| Format | Package | Registration |
| --- | --- | --- |
| CSV, TSV, PSV; inline data | `Packata.ResourceReaders` | Built in |
| Excel | `Packata.ResourceReaders.Excel` | `AddExcel()` |
| Parquet | `Packata.ResourceReaders.Parquet` | `AddParquet()` |
| Fixed-width | `Packata.ResourceReaders.FixedWidth` | `AddFixedWidth()`; also configure layout resolution |
| NDJSON / JSON Lines | `Packata.ResourceReaders.Ndjson` | `AddNdjson()` |
| LTSV / logfmt | `Packata.ResourceReaders.KeyValue` | `AddKeyValueReaders()` |
| Common / W3C logs | `Packata.ResourceReaders.WebLogs` | `AddWebLogs()` |
| Database | `Packata.ResourceReaders.Database` | `AddDatabase()`; also configure the driver or connector |

This table describes .NET package registration. The CLI already configures its
database reader; you do not need to write .NET registration code to preview a
database table from the command line.

Install the packages for the formats your application reads, then add those
providers to a reader factory. For example, an application that reads Excel and
Parquet uses:

```csharp
using Packata.ResourceReaders;
using Packata.ResourceReaders.Excel;
using Packata.ResourceReaders.Parquet;

var readers = ResourceReaderFactory.Create(options => options
    .AddExcel()
    .AddParquet());
```

This factory can also read the built-in formats. Follow the
[.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) to open an asset
through the factory and iterate over its records.

Packata selects a reader using the explicit format, then the media type and file
extension. An unknown declared format causes an error instead of silently being
read as CSV. For an extensionless delimited file, an explicit delimiter can identify
the format.

If the format is recognized but the provider is missing, Packata throws
`ReaderProviderUnavailableException`. Its message names the package and
registration method to add.

## Resolve file locations in a .NET application

Resolve relative data paths against the directory containing the descriptor. If
you resolve them against the application's working directory instead, the same
contract can stop working when you launch the program elsewhere. The
[.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) includes this path
resolution before it opens the reader.

For local directories, HTTP(S), ZIP archives, S3 or Azure storage, use
`Packata.Storages`. Register location schemes and archive wrappers with
`DataPackageLocatorBuilder`, and register resource handlers with
`StorageProviderBuilder`. Your application supplies cloud credentials.

These are library configuration points. Adding a storage backend to an application
does not make its URLs available to every CLI command. If you need custom stream
access through the shared contract model, implement `IEndpointStreamResolver`.

## Configure how values are parsed

A delimited file's dialect describes its delimiter, quote character, line
terminator and header. Use `IDelimitedDialectResolver` when those settings come
from a catalog or sidecar file. A fixed-width reader instead needs a layout
resolver to supply column widths. The provider packages expose their own options;
see the [.NET reference]({{ '/reference/dotnet/' | relative_url }}) for entry points.

Check the chosen provider's support for encoding, compression, repeated headers,
field matching, missing values and numeric conventions. A contract can preserve
a setting even when a particular reader cannot apply it.

Temporal parsing uses the declared format. An offset-bearing strptime format
preserves the offset; a value parsed with an offset-free format remains offset-free.
The [temporal concepts]({{ '/concepts/commitments/' | relative_url }}) explain
these distinctions.

## Add a custom reader

Implement `IDataEndpointReaderProvider` and register it with `AddProvider`.
Custom providers are tried before built-ins, in registration order. Registering
the same provider type twice is rejected, which helps catch accidental duplicate
configuration.

## Release resources when a read finishes

Dispose the streams, readers and containers your application owns, including
when reading fails or stops early. A database connection service can be shared
across readers, but each operation still owns its connection. The
[database guide]({{ '/database-integration/' | relative_url }}#ownership-and-cancellation)
explains which resources belong to the service and which belong to the caller.
