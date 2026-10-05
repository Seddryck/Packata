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
| `Packata.ResourceReaders` | Access to delimited, Excel, Parquet, and database resources through `IDataReader` |
| `Packata.Storages` | Access to documents and resources on local, HTTP(S), S3, and Azure storage |
| `Packata.Provisioners` | Provisioning of canonical data contracts to relational platforms |

Packata targets .NET 8, .NET 9, and .NET 10.

## Installing

Install the package for the document format you use:

```console
dotnet add package Packata.DataPackage
```

```console
dotnet add package Packata.OpenDataContract
```

Add `Packata.ResourceReaders`, `Packata.Storages`, or `Packata.Provisioners` when your application needs those capabilities. Format packages reference `Packata.Core`, so it does not need to be installed separately.

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
