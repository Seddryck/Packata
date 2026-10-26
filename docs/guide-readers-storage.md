---
title: Configure storage and readers
parent: Guides
nav_order: 3
nav_exclude: false
permalink: /guides/readers-storage/
---

A contract describes where its data lives, but loading the contract does not open that data.
Its endpoints provide the locations and format settings, while each asset's binding selects a
particular path or table. When an asset has several bindings, choose the one you want explicitly.

Resolve relative file paths against the directory containing the descriptor. This keeps the
same contract working when you launch your application from a different directory.

## CLI

The commands for validation, inspection, comparison and metadata export can read local files,
HTTP(S) addresses and ZIP archives. Preview needs an unpacked descriptor, either local or remote.
Inference needs a local descriptor, which can be extensionless.

Use the input-format option to select the source standard when automatic detection is not enough.
The accepted values are listed in the [CLI reference]({{ '/cli/' | relative_url }}). Library storage
backends are configured separately, so their availability does not imply that every CLI command
can use them.

CLI preview can read delimited files, inline data, Excel, Parquet and databases.
The fixed-width, NDJSON, key-value and web-log readers are available to .NET applications,
but are not currently registered for CLI preview.

Database preview and inference share the same connection configuration. See the
[connector reference]({{ '/cli-database/' | relative_url }}) to configure it.

## .NET

Install optional packages and register only the formats you need:

```csharp
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Excel;
using Packata.ResourceReaders.FixedWidth;
using Packata.ResourceReaders.KeyValue;
using Packata.ResourceReaders.Ndjson;
using Packata.ResourceReaders.Parquet;
using Packata.ResourceReaders.WebLogs;

var readers = ResourceReaderFactory.Create(options => options
    .AddExcel().AddParquet().AddFixedWidth()
    .AddNdjson().AddKeyValueReaders().AddWebLogs().AddDatabase());
```

| Format | Package / registration |
| --- | --- |
| CSV, TSV, PSV; inline data | `Packata.ResourceReaders` built-ins |
| Excel | `Packata.ResourceReaders.Excel` / `AddExcel()` |
| Parquet | `Packata.ResourceReaders.Parquet` / `AddParquet()` |
| Fixed-width | `Packata.ResourceReaders.FixedWidth` / `AddFixedWidth()`; configure layout resolution |
| NDJSON / JSON Lines | `Packata.ResourceReaders.Ndjson` / `AddNdjson()` |
| LTSV / logfmt | `Packata.ResourceReaders.KeyValue` / `AddKeyValueReaders()` |
| Common / W3C logs | `Packata.ResourceReaders.WebLogs` / `AddWebLogs()` |
| Database | `Packata.ResourceReaders.Database` / `AddDatabase()` and driver/connectors |

Packata chooses a reader using the explicit format, media type and file extension.
If a contract declares an unknown format, reading fails rather than falling back to CSV.
For an extensionless delimited file, an explicit delimiter can identify the format.

If Packata recognizes the format but its reader has not been registered, it throws
`ReaderProviderUnavailableException`. The exception suggests the package and registration
method you need.

Use `Packata.Storages` to configure access to local directories, HTTP(S), ZIP archives,
S3 or Azure. Register the schemes and archive wrappers with `DataPackageLocatorBuilder`,
and register resource handlers with `StorageProviderBuilder`. Your host application supplies
the cloud credentials.

For custom canonical stream access, implement `IEndpointStreamResolver`. These library extension
points do not add cloud descriptor URLs to CLI commands.

## Configuration and ownership

To add a reader, register an `IDataEndpointReaderProvider` with `AddProvider`.
Custom providers run before the built-ins, in the order you register them. Registering the
same provider type twice is rejected.

Parsing can also be configured independently of reader selection. An `IDelimitedDialectResolver`
can obtain a dialect from a catalog or sidecar file, while a fixed-width layout resolver supplies
column widths. Use the public options in the corresponding provider package; the
[.NET reference]({{ '/reference/dotnet/' | relative_url }}) links to those APIs.

For delimited data, the dialect controls the delimiter, quote character, line terminator and
header. Other behavior, such as encoding, compression and repeated headers across multiple
paths, depends on the reader provider.

Check your provider's support for field matching, missing values, numeric conventions and
logical-to-runtime type mapping. Preserving a setting in a contract does not guarantee that
every reader applies it. Temporal parsing follows declared formats: an offset-bearing strptime
format preserves the offset, while an offset-free value remains offset-free. See
[temporal concepts]({{ '/concepts/commitments/' | relative_url }}) for details.

Dispose the streams, readers and containers your application owns, including when a read fails
or stops early. A shared database service shares configuration, not a live connection.
[Database integration]({{ '/database-integration/' | relative_url }}) explains the ownership,
callback precedence and cancellation rules.