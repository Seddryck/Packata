---
title: Installation
parent: Getting started
nav_order: 4
nav_exclude: false
permalink: /installation/
---

Choose the CLI if you want to work in a terminal, or the .NET libraries if you
want to call Packata from application code. You do not need to install both.
The [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) is the default path
for trying Packata with a small sample.

## Command-line tool

Use a .NET 10 or later SDK for the `dotnet tool` commands below. The RID-aware
NuGet package format is not supported by the .NET 8 or 9 SDK. The installed tool
runs on compatible ASP.NET Core and .NET runtimes; the .NET CLI chooses the host platform and a
compatible target framework. If you want an executable that includes its runtime,
use a self-contained [standalone archive](#standalone-archives) instead.

### Install for your user account

A global tool installation makes `packata` available from any working directory:

```console
dotnet tool install --global Packata-cli
packata --help
packata --version
```

Help lists the command groups, and the version command confirms which build is
installed. To update an existing global installation, run
`dotnet tool update --global Packata-cli`. If your terminal cannot find `packata`,
follow the tool installer's PATH instructions for your shell.

### Install for a repository

A local tool manifest lets a repository record the tool it needs. In that
repository's directory, create a manifest if one does not already exist, then
install Packata:

```console
dotnet new tool-manifest
dotnet tool install Packata-cli
dotnet tool run packata -- --help
```

Skip `dotnet new tool-manifest` when the repository already has one. If Packata
is already recorded in a checked-in manifest, use `dotnet tool restore` instead
of installing it again. Run local-tool commands from the manifest's directory
or one of its descendants.

After either installation, follow the
[CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) to check a contract and
read sample records. The [CLI reference]({{ '/cli/' | relative_url }}) contains
the complete command and option descriptions.

## Standalone archives

Use an archive when you want to run an executable directly rather than install
a .NET tool. [GitHub releases](https://github.com/Seddryck/Packata/releases/latest)
provide two archive families:

| Archive name | What you need |
| --- | --- |
| `Packata-cli-<version>-<framework>-<rid>` | The corresponding ASP.NET Core runtime (includes .NET): `net8.0`, `net9.0` or `net10.0` |
| `Packata-cli-<version>-<rid>` | No separate .NET installation; the self-contained archive includes .NET 10 |

Here, `<rid>` identifies the operating system and processor architecture. Choose
it from the table below; `<version>` is the release version you are downloading.

| Platform | RID | Executable |
| --- | --- | --- |
| Windows x64 | `win-x64` | `packata.exe` |
| Windows ARM64 | `win-arm64` | `packata.exe` |
| Linux x64 (glibc) | `linux-x64` | `packata` |
| Linux ARM64 (glibc) | `linux-arm64` | `packata` |
| Alpine Linux x64 (musl) | `linux-musl-x64` | `packata` |
| Alpine Linux ARM64 (musl) | `linux-musl-arm64` | `packata` |
| macOS Intel | `osx-x64` | `packata` |
| macOS Apple Silicon | `osx-arm64` | `packata` |

Extract the ZIP on Windows or the `tar.gz` on Linux/macOS. From the extracted
directory, invoke `packata.exe` or `packata` using your shell's syntax for a local
executable, with `--version` to check it starts.

Choose `linux-musl-*` for musl-based distributions such as Alpine, and `linux-*`
for glibc-based distributions. There is no 32-bit Windows CLI archive.

A self-contained archive includes .NET, but a database connector or native vendor
driver may still have its own prerequisites. The
[database guide]({{ '/database-integration/' | relative_url }}) explains how
connectors are selected and acquired.

## .NET libraries

Use a .NET 8, 9 or 10 SDK and open a terminal in your application project's
directory. Add the package for the contract standard you need:

```console
dotnet add package Packata.DataPackage
```

For ODCS documents, add `Packata.OpenDataContract` instead. Add both if your
application handles both standards. Each format package brings in `Packata.Core`,
which contains the shared contract model; it does not need a separate package
command for this setup.

To read dataset records, also add the base reader package:

```console
dotnet add package Packata.ResourceReaders
```

This includes delimited-file and inline-data readers. The
[reader registration table]({{ '/guides/readers-storage/' | relative_url }}#choose-how-to-read-the-dataset)
lists optional packages for Excel, Parquet, databases and other formats, together
with the registration each one needs.

Add `Packata.Storages` for library storage backends, `Packata.Database` for shared
database connection configuration, or `Packata.Provisioners` for database
provisioning. Install these when your application needs those operations; they
are not required for the basic CSV walkthrough.

Continue with the [.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }})
for a complete application, or the [.NET reference]({{ '/reference/dotnet/' | relative_url }})
for package and API entry points.

## VS Code status

The extension is **planned, not implemented**. There is no extension to install
yet. The [VS Code status page]({{ '/quickstart/vscode/' | relative_url }}) links
to the proposals and to the CLI and .NET walkthroughs you can use today.
