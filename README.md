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
| `Packata.ResourceReaders.Ndjson` | Optional NDJSON and JSON Lines reader provider |
| `Packata.ResourceReaders.Parquet` | Optional Parquet reader provider |
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
    .AddNdjson()
    .AddParquet()
    .AddDatabase());
```

The default reader recognizes CSV, TSV, and PSV by format name, media type, or file extension. An explicit
`delimiter` option also identifies extensionless data as delimited; it does not override an unknown explicit format.

The NDJSON provider recognizes `ndjson`, `jsonl`, `application/x-ndjson`, and `application/ndjson`. With a
canonical schema, its field order follows that schema, additional properties are ignored, and missing or JSON
`null` properties return `DBNull.Value`. Without a schema, each record exposes its properties in source order.

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
