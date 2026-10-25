---
title: Configure storage and readers
parent: Guides
nav_order: 3
nav_exclude: false
permalink: /guides/readers-storage/
---

Locate the descriptor separately from its data. Canonical endpoints contain locations and format metadata; bindings select asset-specific paths/tables. Resolve relative file paths against the descriptor's directory, not an arbitrary working directory. Choose a binding explicitly when several exist.

## CLI

Validation, inspection, diff, compatibility comparison and metadata exports accept local and HTTP(S) descriptors and ZIP packages. Preview accepts unpacked local/HTTP(S) descriptors; inference requires a local descriptor, including an extensionless file. Every contract-reading command supports `--input-format auto|odcs|datapackage`. Do not assume library storage backends are available in every command. See [source and output contracts]({{ '/cli/' | relative_url }}).

The CLI preview registers delimited/inline, Excel, Parquet and database readers. Fixed-width, NDJSON, key-value and web-log providers are library options, not currently registered CLI preview formats. Database configuration is shared by preview and inference; see [connector reference]({{ '/cli-database/' | relative_url }}).

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

Format resolution considers explicit format, media type and path extension. An unknown explicit format fails instead of being treated as CSV. An explicit delimiter can identify extensionless delimited data. `ReaderProviderUnavailableException` distinguishes a recognized format with a missing provider and suggests the package/registration.

`Packata.Storages` supplies locator/container builders for local directories, HTTP(S), ZIP, S3 and Azure. Register schemes/wrappers explicitly with `DataPackageLocatorBuilder` and resource handlers with `StorageProviderBuilder`; supply cloud credentials through host configuration. Use `IEndpointStreamResolver` for custom canonical stream resolution. Cloud storage support does not mean the CLI accepts cloud descriptor URLs.

## Configuration and ownership

Custom `IDataEndpointReaderProvider` instances registered by `AddProvider` run in registration order before built-ins; duplicate provider types are rejected. `IDelimitedDialectResolver` can resolve a catalog/sidecar dialect, and fixed-width layout resolution supplies column widths. Use the corresponding provider package's public options rather than guessing format-specific parameters. See [.NET source/API links]({{ '/reference/dotnet/' | relative_url }}).

Delimited settings include delimiter, quote, line terminator and header. Encoding/compression and multi-path/header behavior depend on the provider. Logical/runtime type mapping, field matching, missing values, numeric conventions and temporal formats must be checked for that provider; metadata preservation alone does not prove it applies every parser option. Declared offset-bearing strptime formats preserve offsets; offset-free values remain offset-free. See [temporal concepts]({{ '/concepts/commitments/' | relative_url }}).

Dispose owned streams, readers and containers, including on failure or early termination. Database services own configuration, not a shared live connection; [database integration]({{ '/database-integration/' | relative_url }}) defines callback precedence, cancellation and disposal.
