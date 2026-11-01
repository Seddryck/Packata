---
name: install-local-cli
description: Install the framework-dependent .NET 10 Windows x64 Packata CLI locally from a GitHub pull request or release into a user-specified directory. Use for downloaded CLI distributions, not building from source or installing the .NET tool globally.
---

# Install Packata CLI locally

Use `scripts/install-local.ps1` with PowerShell 7 and authenticated GitHub CLI access (`gh auth status -h github.com`). Defaults target `Seddryck/Packata` and its lightweight framework-dependent .NET 10 Windows x64 ZIP. The matching x64 ASP.NET Core 10 runtime (including the .NET runtime) must already be installed; the archive does not bundle it.

Run from this skill directory, or invoke the script by its full path.

- Open PR: `./scripts/install-local.ps1 -InstallDirectory <path> -PullRequest <number>` selects the newest successful `ci-release.yml` run for the PR head SHA and the single unexpired `cli-framework-net10.0-win-x64-*` artifact.
- Merged PR: the same command resolves a published release whose tag points to the merge commit. Closed, unmerged PRs are not installable.
- Named release: `./scripts/install-local.ps1 -InstallDirectory <path> -Release <tag>`.
- Latest release: `./scripts/install-local.ps1 -InstallDirectory <path>`.

The destination must be empty or absent unless the user explicitly wants to reuse it with `-Force`. Forced installation may overwrite archive files while retaining unrelated files. Existing `config.json`, `appsettings.json`, and lowercase `packata.config.json` files within the installation are preserved byte-for-byte.

Packata's actual user configuration is outside the installation: `%APPDATA%/Packata/config.json`, or `config.json` under `PACKATA_CONFIG_HOME` (see `src/Packata.Cli/Services/CliConfiguration.cs`). Never reset that configuration as an installation step. The installer does not change environment variables, PATH, or user configuration outside the destination.

The script downloads exactly one matching ZIP, stages it in a unique temporary directory, copies its files into the destination, and verifies exactly one `packata.exe` exists. Temporary downloads are cleaned up. Report the PR run or release tag and executable path. Explain missing/expired/ambiguous artifacts or missing commit-matching releases without falling back to another source.

## Parameters and casing

| Parameter | Default / behavior |
|---|---|
| `-ProductName` | `Packata`; executable and conventional config filenames use lowercase `packata` |
| `-Repository` | `Seddryck/Packata`; retain supplied casing |
| `-ArchivePattern` | `Packata-cli-*-net10.0-win-x64.zip`; the archive prefix intentionally retains uppercase P |
| `-ArtifactName` | `cli-framework-net10.0-win-x64-*`; case-sensitive wildcard pattern for versioned PR artifact names |
| `-Workflow` | `ci-release.yml` |
| `-ConfigurationFiles` | `config.json`, `appsettings.json`; basenames preserved in addition to `<product>.config.json` |

Only ZIP distributions containing a Windows executable are supported. Use the framework-dependent .NET 10 Windows x64 distribution by default. Keep normal invocations about Packata; the script's independent parameters support reuse without editing product literals.
