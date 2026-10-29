# CLI NuGet packaging

`package-cli-framework-archives` publishes and smoke-tests the CLI once per
framework/RID. `package-cli-nuget` downloads those immutable archives, validates
and extracts them, and runs `dotnet pack --no-build` against
`Packata.Cli.Package.csproj`. This metadata-only project has no source or project
references and disables publishing. The SDK still generates the tool settings,
pointer, package types, and NuGet metadata. Payload hashes are checked against
the extracted archives before upload.

| Dimension | Evidence and decision |
| --- | --- |
| Frameworks | Preserve `net8.0`, `net9.0`, and `net10.0` from `Directory.Build.props`. |
| CLI unit tests | Restore, build, and test each framework in an independent matrix job. Archive jobs wait for the complete matrix, preserving the existing test gate while avoiding sequential framework runs. |
| Runtime distributions | Preserve all eight declared RIDs, including glibc/musl and x64/ARM64. |
| Package identity | Preserve `Packata-cli` plus eight `Packata-cli.<rid>` packages at one release version. Each RID package contains all three frameworks. |
| Build inputs | Use the validated framework archives, README, and existing icon; no new generated prerequisites or packaging tools. |
| Installation tests | SDK 10 installs from an isolated candidate-only feed on every framework/RID. Native runners cover Windows, Linux, and macOS; Alpine SDK containers cover musl. Run help and version through the installed shim. |
| Older SDKs | SDK 8 and 9 cannot consume the version-2 RID-aware pointer. Explicitly pin each SDK and verify its compatibility rejection on Windows and Linux. This does not remove .NET 8/9 runtime payloads or standalone archives. |
| Publishing | Preserve main-only NuGet publication, patch-zero GitHub releases, versions, and permission boundaries. Complete artifact validation now also waits for installation checks. |

The compatibility tests use a `global.json` with `rollForward: disable` and check
`dotnet --version`. Package sources and caches are isolated to prevent public
packages or hosted-runner SDK selection from masking a broken candidate.

To reproduce package assembly, download the `cli-framework-*` archives from one
CI run into a single directory and run from the repository root:

```powershell
./.github/scripts/Expand-CliFrameworkArchives.ps1 -Version <version> -ArchiveDirectory <archives> -OutputDirectory <payloads>
dotnet restore .github/packaging/Packata.Cli.Package.csproj
dotnet pack .github/packaging/Packata.Cli.Package.csproj -c Release --no-build --no-restore -o <packages> -p:Version=<version> -p:CliPublishRoot=<absolute-payloads>
./.github/scripts/Validate-CliArtifacts.ps1 -Version <version> -NuGetDirectory <packages> -FrameworkPayloadDirectory <payloads> -SkipFrameworkDependentArchives -SkipSelfContainedArchives
```
