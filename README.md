# Packata

![Logo](https://raw.githubusercontent.com/Seddryck/Packata/main/assets/packata-icon-256.png)

Packata is a standards-neutral .NET library for working with Data Package v2 and Open Data Contract Standard (ODCS) documents. It maps both formats to a shared model for data assets, schemas, metadata, and endpoints, with optional packages for reading and provisioning data.

[About][] | [Packages][] | [Installing][] | [Quickstart][]

[About]: #about (About)
[Packages]: #packages (Packages)
[Installing]: #installing (Installing)
[Quickstart]: #quickstart (Quickstart)

## About

Use the format-specific models when you need direct access to Data Package or ODCS concepts. Map either format to `Packata.Core.Contracts.DataContract` when you want readers, provisioners, and other integrations to work independently of the source document format.

**Social media:** [![website](https://img.shields.io/badge/website-seddryck.github.io/Packata-fe762d.svg)](https://seddryck.github.io/Packata)
[![twitter badge](https://img.shields.io/badge/twitter%20Packata-@Seddryck-blue.svg?style=flat&logo=twitter)](https://twitter.com/Seddryck)

**Releases:** [![GitHub releases](https://img.shields.io/github/v/release/seddryck/packata?label=GitHub%20releases)](https://github.com/seddryck/packata/releases/latest)
[![nuget](https://img.shields.io/nuget/v/Packata.Core.svg)](https://www.nuget.org/packages/Packata.Core/) [![GitHub Release Date](https://img.shields.io/github/release-date/seddryck/Packata.svg)](https://github.com/Seddryck/Packata/releases/latest) [![licence badge](https://img.shields.io/badge/License-Apache%202.0-yellow.svg)](https://github.com/Seddryck/Packata/blob/main/LICENSE)

**Dev. activity:** [![GitHub last commit](https://img.shields.io/github/last-commit/Seddryck/Packata.svg)](https://github.com/Seddryck/Packata/commits)
![Still maintained](https://img.shields.io/maintenance/yes/2026.svg)
![GitHub commit activity](https://img.shields.io/github/commit-activity/y/Seddryck/Packata)

**Continuous integration builds:** [![CI and release](https://github.com/Seddryck/Packata/actions/workflows/ci-release.yml/badge.svg)](https://github.com/Seddryck/Packata/actions/workflows/ci-release.yml)
[![CodeFactor](https://www.codefactor.io/repository/github/seddryck/Packata/badge)](https://www.codefactor.io/repository/github/seddryck/Packata)
[![codecov](https://codecov.io/github/Seddryck/Packata/branch/main/graph/badge.svg?token=PPSNKG5YD7)](https://codecov.io/github/Seddryck/Packata)
<!-- [![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FSeddryck%2FPackata.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FSeddryck%2FPackata?ref=badge_shield) -->

**Status:** [![stars badge](https://img.shields.io/github/stars/Seddryck/Packata.svg)](https://github.com/Seddryck/Packata/stargazers)
[![Bugs badge](https://img.shields.io/github/issues/Seddryck/Packata/bug.svg?color=red&label=Bugs)](https://github.com/Seddryck/Packata/issues?utf8=%E2%9C%93&q=is:issue+is:open+label:bug+)
[![Top language](https://img.shields.io/github/languages/top/seddryck/Packata.svg)](https://github.com/Seddryck/Packata/search?l=C%23)

## Packages

| Package | Purpose |
|---|---|
| `Packata.Core` | Standards-neutral contracts for assets, schemas, endpoints, diagnostics, readers, and provisioners |
| `Packata.Database` | Shared DubUrl connection configuration and runtime endpoint resolution |
| `Packata.DataPackage` | Data Package v2 models, JSON/YAML serialization, validation, and canonical mapping |
| `Packata.OpenDataContract` | ODCS models, YAML serialization, validation, and canonical mapping |
| `Packata.ResourceReaders` | Core reader factory and access to delimited resources through `IDataReader` |
| `Packata.ResourceReaders.Excel` | Optional Excel reader provider |
| `Packata.ResourceReaders.FixedWidth` | Optional fixed-width text reader provider |
| `Packata.ResourceReaders.KeyValue` | Optional LTSV and logfmt reader provider |
| `Packata.ResourceReaders.Ndjson` | Optional NDJSON and JSON Lines reader provider |
| `Packata.ResourceReaders.Parquet` | Optional Parquet reader provider |
| `Packata.ResourceReaders.WebLogs` | Optional Common and W3C web-log reader provider |
| `Packata.ResourceReaders.Database` | Optional database reader provider |
| `Packata.Storages` | Access to documents and resources on local, HTTP(S), S3, and Azure storage |
| `Packata.Provisioners` | Provisioning of canonical data contracts to relational platforms |
| `Packata-cli` | Cross-platform command-line interface distributed as a RID-aware .NET tool and release archives |

Packata libraries and the CLI target .NET 8, .NET 9, and .NET 10.

## Installing

Install the package for the document format you use:

```console
dotnet add package Packata.DataPackage
```

```console
dotnet add package Packata.OpenDataContract
```

Add `Packata.ResourceReaders`, `Packata.Storages`, or `Packata.Provisioners` when your application needs those capabilities. Format packages reference `Packata.Core`, so it does not need to be installed separately.

Install only the resource-reader providers your application uses, then register them when creating the factory:

```csharp
var readers = ResourceReaderFactory.Create(options => options
    .AddExcel()
    .AddFixedWidth()
    .AddKeyValueReaders()
    .AddNdjson()
    .AddParquet()
    .AddWebLogs()
    .AddDatabase());
```

The default reader recognizes CSV, TSV, and PSV by format name, media type, or file extension. An explicit
`delimiter` option also identifies extensionless data as delimited; it does not override an unknown explicit format.
Data Package Table Dialect metadata maps directly to the canonical `delimiter`, `quoteChar`, `lineTerminator`, and
`header` options. Applications can register an `IDelimitedDialectResolver` when the dialect comes from a catalog,
sidecar file, configuration, or another source:

```csharp
var readers = ResourceReaderFactory.Create(options =>
{
    options.Formats.AddExtension(".dat", "csv");
    options.AddDelimited(delimited =>
        delimited.AddDialectResolver(new CatalogDialectResolver()));
});
```

Custom resolvers run before canonical format options. When no resolver supplies a dialect, CSV uses commas, TSV
uses tabs, and PSV uses pipes. ODCS local-file servers do not define a standard CSV dialect. Packata therefore maps
`delimiter`, `quoteChar`, `lineTerminator`, and `header` only from ODCS custom properties whose `vendor` is
`packata`. A custom ODCS server's own `delimiter` is also mapped when its format is CSV, TSV, or PSV. The delimiter
on Azure, S3, and SFTP servers remains JSON-document metadata and is not reinterpreted as a CSV field delimiter.

An unknown CSV can also bootstrap a canonical contract. Sampling, header and dialect detection, type inference,
and nullability inference are delegated to PocketCsvReader; inspect the returned diagnostics before accepting
heuristic decisions:

```csharp
using Packata.ResourceReaders;

var result = new CsvDataContractInference().Infer("customers.csv");
foreach (var diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");

var contract = result.Contract;
```

Pass `CsvDataContractInferenceOptions` to configure PocketCsvReader sampling, culture, header and dialect inference,
or to override generated contract, asset, and endpoint metadata.

An existing relational database can bootstrap the same canonical model. Database metadata discovery is delegated
to DubUrl, while Packata maps tables, supported views, fields, keys, relationships, constraints, and diagnostics.
Install `Packata.ResourceReaders.Database` plus the DubUrl provider package for the database being inspected:

```csharp
using DubUrl;
using Packata.ResourceReaders.Database;

var connection = new ConnectionUrl(Environment.GetEnvironmentVariable("DATABASE_URL")!);
var result = await new DatabaseDataContractInference().InferAsync(connection);

foreach (var diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");

var contract = result.RequireContract();
```

`DatabaseDataContractInferenceOptions` forwards DubUrl discovery filters and completeness settings and can override
generated contract, asset, and endpoint metadata. Returned endpoints omit URL credentials and query options; bind
runtime credentials through application configuration rather than persisting them in a contract.

The NDJSON provider recognizes `ndjson`, `jsonl`, `application/x-ndjson`, and `application/ndjson`. With a
canonical schema, its field order follows that schema, additional properties are ignored, and missing or JSON
`null` properties return `DBNull.Value`. Without a schema, each record exposes its properties in source order.

The fixed-width provider canonicalizes `fixedwidth`, `fwf`, and `text/x-fixed-width` to `fixed-width`. Applications
can map an additional declared name or ambiguous file extension explicitly:

```csharp
var readers = ResourceReaderFactory.Create(options =>
{
    options.Formats
        .AddAlias("legacy-fixed", DataFormatNames.FixedWidth)
        .AddExtension(".dat", DataFormatNames.FixedWidth);
    options.AddFixedWidth();
});
```

Explicit endpoint format metadata takes precedence over extension inference. By default, fixed-width layout comes
from a canonical schema plus a `widths` format option aligned with its fields. Optional `offsets` and `recordWidth`
options describe non-contiguous layouts and their bounds; invalid, overlapping, or out-of-range fields fail before
reading. Short records fail, while long records require `allowTrailingCharacters: true`. Applications can register
an `IFixedWidthLayoutResolver` through `AddFixedWidth` to derive layouts from ODCS field extensions, copybooks,
sidecar files, configuration, or another metadata source before falling back to the format options.

ODCS contracts should declare `format: fixed-width` on the file server. A custom resolver can interpret preserved
field metadata using the Packata convention below, where offsets are zero-based and lengths count characters:

```yaml
servers:
  - server: customers-file
    type: local
    path: ./customers.dat
    format: fixed-width
schema:
  - name: customers
    physicalType: file
    properties:
      - name: customerId
        logicalType: integer
        customProperties:
          - { vendor: packata, property: fixedWidthOffset, value: 0 }
          - { vendor: packata, property: fixedWidthLength, value: 8 }
```

The key-value provider recognizes LTSV (`ltsv`, `text/x-ltsv`, `text/ltsv`) and logfmt (`logfmt`, `log-fmt`,
`application/logfmt`, `text/x-logfmt`). A canonical schema fixes field order and types; otherwise, the first record
fixes the columns for the stream, later missing keys return `DBNull.Value`, and later new keys are ignored. Duplicate
keys from the first record remain separate columns and name lookup resolves the first occurrence. Quoting and escaping
follow the selected PocketCsvReader format.

Optional text providers honor the endpoint encoding and compression settings, read multiple paths in their declared
order, propagate cancellation while opening resources, and transfer stream cleanup to the returned reader.

The web-log provider treats Common Log Format (`common-log`, `commonlog`, `clf`, `text/x-common-log`) and W3C
Extended Log Format (`w3c-log`, `w3c`, `w3c-extended`, `text/x-w3c-log`) as distinct formats. Common logs expose
`RemoteHost`, `Identity`, `AuthenticatedUser`, `Timestamp`, `Request`, `StatusCode`, and `ResponseBytes`; status and
byte counts are numeric and `-` is `DBNull.Value`. For W3C logs, the `#Fields` directive fixes column names and order,
and standard numeric fields are typed by PocketCsvReader. Data before `#Fields`, changing schemas, and malformed
records fail with line-aware diagnostics.

### Command-line tool

Install the CLI from NuGet:

```console
dotnet tool install --global Packata-cli
packata --help
```

Validate and inspect ODCS or Data Package v2 contracts, then preview an asset's bound data:

```console
packata contract validate contract.yaml
packata contract inspect contract.yaml --asset customers --output json
packata data preview contract.yaml --asset customers --limit 20
```

See the [CLI reference](docs/cli.md) for source formats, output representations, exit codes,
row limits, column selection, and examples for redirected output.

Infer a bound CSV asset from an ODCS or Data Package v2 document. The default command only displays the inferred
schema, dialect, diagnostics, and confidence; it does not modify the contract:

```console
packata schema infer datapackage.json --asset customers
```

Write an enriched copy with `--output`, or explicitly update the source document atomically with `--write`:

```console
packata schema infer datapackage.json --asset customers --output enriched-datapackage.json
packata schema infer contract.yaml --asset customers --write
```

Use `--maximum-records`, `--maximum-bytes`, `--culture`, `--header`, and `--no-dialect-inference` to control the
bounded PocketCsvReader inference. The CSV location, format, encoding, compression, and existing dialect hints are
resolved from the selected asset's endpoint binding.

The same command infers a bound database table or view when the selected asset resolves to a database endpoint:

```console
packata schema infer contract.yaml --asset customers
packata schema infer contract.yaml --asset customers --output enriched-contract.yaml
```

Database connection details and the physical object name are resolved from the endpoint binding. Discovery is
delegated to DubUrl; the CLI loads installed connectors or acquires them through a configured curated distribution catalog. CSV-only sampling and
dialect options are ignored for database assets. Non-sensitive connection query options are retained in inferred
metadata, sensitive query values are redacted, authority credentials are omitted, and URL fragments are removed.

Manage connectors with `packata connector list`, `packata connector show <id>`, and
`packata connector install <id>`. Configure download policy once with
`packata config set connectors.offline true`; available connectors and database network connections still work.
`packata config list` shows effective settings and their origins. The original `packata infer` spelling
remains a compatibility alias. See [database CLI commands and configuration](docs/cli-database.md) for all actions,
settings, connector preferences, and runtime connection environment overrides.
DubUrl 0.43.0's bundled catalog has metadata only; downloads currently require setting
`connectors.catalog` to a compatible distribution catalog containing archive URLs and hashes.

Update an existing installation with `dotnet tool update --global Packata-cli`.

The package is a RID-aware .NET tool: the .NET CLI automatically selects the package matching the host operating system and architecture, then uses a compatible .NET 8, .NET 9, or .NET 10 target.

The [GitHub releases](https://github.com/Seddryck/Packata/releases/latest) page provides two archive types for every supported platform:

- Framework-dependent archives named `Packata-cli-<version>-<framework>-<rid>` target `net8.0`, `net9.0`, or `net10.0` and require the corresponding .NET runtime.
- Self-contained .NET 10 archives named `Packata-cli-<version>-<rid>` include the runtime and preserve the original release filenames.

Select the RID matching the target platform:

| Platform | Runtime archive | Executable |
|---|---|---|
| Windows x64 | `win-x64` | `packata.exe` |
| Windows ARM64 | `win-arm64` | `packata.exe` |
| Linux x64 (glibc) | `linux-x64` | `packata` |
| Linux ARM64 (glibc) | `linux-arm64` | `packata` |
| Alpine Linux x64 (musl) | `linux-musl-x64` | `packata` |
| Alpine Linux ARM64 (musl) | `linux-musl-arm64` | `packata` |
| macOS Intel | `osx-x64` | `packata` |
| macOS Apple Silicon | `osx-arm64` | `packata` |

Linux distributions using glibc must use a `linux-*` archive; Alpine and other musl-based distributions must use `linux-musl-*`. Extract the ZIP archive on Windows or the `tar.gz` archive on Linux/macOS, then invoke the executable directly. Packata does not publish a 32-bit Windows CLI.

## Quickstart

Load a Data Package descriptor and map it to the standards-neutral model:

```csharp
using Packata.DataPackage;
using Packata.DataPackage.Mapping;

var package = new DataPackageFactory().LoadFromFile("datapackage.json");

var contract = package
    .ToCanonicalContract()
    .ReportDiagnostics(diagnostic =>
        Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}"))
    .RequireValue();

foreach (var asset in contract.Assets)
    Console.WriteLine(asset.Name);
```

ODCS documents map to the same canonical model through the `Packata.OpenDataContract.Mapping` namespace. See [MIGRATION.md](MIGRATION.md) for side-by-side mapping examples and guidance for upgrading from earlier Packata versions.

For shared database connections, host-supplied credentials, and operation preflight, see
[database integration](docs/database-integration.md) and the runnable
[ODCS and Data Package example](examples/DatabaseConnections). The
[connector acquisition example](examples/ConnectorAcquisition) demonstrates acquisition on the first read
from a host-supplied catalog and reuse on later reads.

## License

Packata is licensed under the [Apache License 2.0](LICENSE).
