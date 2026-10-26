---
title: Installation
parent: Getting started
nav_order: 4
nav_exclude: false
permalink: /installation/
---

## .NET libraries

Use a .NET 8, 9 or 10 SDK and add the native format package your application needs:

```console
dotnet add package Packata.DataPackage
dotnet add package Packata.OpenDataContract
```

Both format packages already reference `Packata.Core`. To read data, add
`Packata.ResourceReaders` and the optional providers you need. For storage, database
connections or provisioning, add `Packata.Storages`, `Packata.Database` or
`Packata.Provisioners` respectively.

Use the [package and API reference]({{ '/reference/dotnet/' | relative_url }}) to choose entry
points and the [reader registration table]({{ '/guides/readers-storage/' | relative_url }})
to select format-specific packages.

## Command-line tool

A compatible .NET 8, 9 or 10 runtime is required for the framework-dependent tool. The .NET CLI selects the host RID and a compatible target framework.

```console
dotnet tool install --global Packata-cli
packata --help
packata --version
```

Update an existing global installation with `dotnet tool update --global Packata-cli`. If the executable is not found, follow the tool installer's PATH instructions for your shell.

For a repository-local installation, run in its directory:

```console
dotnet new tool-manifest
dotnet tool install Packata-cli
dotnet tool run packata -- --help
```

Create the manifest only if one does not exist. Restore a checked-in manifest with `dotnet tool restore`. Follow the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) after installation. Every [command and option]({{ '/cli/' | relative_url }}) is documented separately from this installation page.

## Standalone archives

[GitHub releases](https://github.com/Seddryck/Packata/releases/latest) provide two archive families:

- Framework-dependent `Packata-cli-<version>-<framework>-<rid>` archives target `net8.0`, `net9.0`, or `net10.0` and require that runtime.
- Self-contained .NET 10 `Packata-cli-<version>-<rid>` archives include the runtime.

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

Extract the ZIP archive on Windows or the `tar.gz` archive on Linux/macOS, then invoke the executable directly. Choose `linux-musl-*` for musl-based distributions and `linux-*` for glibc. Packata does not publish a 32-bit Windows CLI.

A self-contained executable bundles .NET, but database connector/native vendor drivers can still have separate prerequisites. See [connector configuration]({{ '/cli-database/' | relative_url }}).

## VS Code status

**Planned — not implemented.** There is no extension to install yet. See the [planned Quickstart and roadmap]({{ '/quickstart/vscode/' | relative_url }}).
