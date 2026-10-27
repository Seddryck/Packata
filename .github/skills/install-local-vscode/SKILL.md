---
name: install-local-vscode
description: Install Packata Language Support from a GitHub PR or release VSIX into VS Code Insiders by default. Use for installing or testing the Packata extension, not installing the VS Code application or building the extension.
---

# Install Packata into VS Code Insiders

Use `scripts/install-local.ps1` with PowerShell 7, authenticated GitHub CLI access (`gh auth status -h github.com`), and an existing VS Code Insiders installation. The default editor command is `code-insiders`. Do not silently switch to Stable or install the editor application if Insiders is missing; explain how to supply its command path.

## Source selection

Specify the PR you want to deploy locally. Any open PR can be selected, provided its current head has a successful workflow run that produces the matching VSIX artifact:

`./scripts/install-local.ps1 -PullRequest <number>`

Replace <number> with your chosen PR number. Run from this skill directory, or invoke the script by its full path. The script resolves that PR's current head SHA, finds its newest successful `ci-release.yml` pull-request run, and downloads the single unexpired `vscode-win32-x64-*` artifact. Version suffixes vary per run.

A merged PR resolves a published release whose tag points to the merge commit. If no matching release/VSIX exists, explain the condition; do not substitute another build. Closed, unmerged PRs are not installable.

For a release containing the VSIX, use `-Release <tag>` or omit source options for the latest release. If the selected release has no matching VSIX, explain the missing artifact without switching to a PR or another release.

## Install and verify

The default Windows x64 package is `packata-language-support-*-win32-x64.vsix`, containing a self-contained language server. The script reads `extension/package.json` from the VSIX and requires identity `seddryck.packata-language-support` before installation.

It calls `code-insiders --install-extension <downloaded-vsix>`, then `code-insiders --list-extensions --show-versions` to verify the package's exact extension version. Report the source run/release, version, and editor command. Remind the user to reload the editor window after an update. Preserve editor settings and workspace files.

Use `-Force` only when the user explicitly requests replacement/reinstallation; this forwards `--force` to the editor. `-Channel Stable` explicitly selects `code` instead. `-CodeCommand <full-path-to-code-insiders.cmd>` overrides command discovery; use it for a non-PATH installation. Never infer Stable from a missing Insiders command.

Repository, workflow, artifact, and VSIX filename patterns are independently configurable through `-Repository`, `-Workflow`, `-ArtifactName`, and `-ArchivePattern`. Retain their casing. Artifact matching is case-sensitive and rejects multiple matches. Defaults are Windows x64; another platform needs a compatible VSIX and editor. Temporary downloads are deleted on success or failure.

This skill installs the Packata extension into the existing editor. It does not install VS Code itself.
