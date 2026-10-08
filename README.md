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
| `Packata-cli` | Cross-platform command-line interface distributed as a .NET tool and self-contained executables |

Packata libraries target .NET 8, .NET 9, and .NET 10. The CLI targets .NET 10.

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

Install the framework-dependent .NET 10 tool from NuGet:

```console
dotnet tool install --global Packata-cli
packata --help
```

Update an existing installation with `dotnet tool update --global Packata-cli`.

Self-contained executables that do not require an installed .NET runtime are available from the [GitHub releases](https://github.com/Seddryck/Packata/releases/latest) page. Select the archive for your platform:

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

## License

Packata is licensed under the [Apache License 2.0](LICENSE).
