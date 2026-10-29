# Packata Language Support

Navigate ODCS v3.2 and Frictionless Data Package v2 contracts in the **Packata → Data Contracts** Activity Bar view. The native tree discovers descriptors across workspace folders, displays canonical assets and columns, and preserves format-specific metadata. Context menus provide **Open Source**, **Inspect Contract**, **Validate Contract**, and **Copy Logical Path**. Click a source-backed item to reveal its definition.

Open buffers take precedence over saved descriptors, including unsaved edits. Constraints, quality declarations, and SLA commitments remain separate. Source quality declarations are labelled as such; the extension does not execute quality rules. No empty quality/SLA sections are invented. Mapping diagnostics remain visible even when some source information cannot be represented canonically; use Open Source to inspect the complete descriptor.

Inspection is distinct from validation. A successful tree load does not display a passing validation status. Explicit validation reports through VS Code Problems, using the same server diagnostic collection as automatic document checks. Resource preview is tracked separately by [#451](https://github.com/Seddryck/Packata/issues/451).

## Install

Install the VSIX for your platform with **Extensions: Install from VSIX**. Each package bundles its own .NET server and runtime; a separate .NET or Packata CLI installation is unnecessary.

| VSIX target | Server RID | Runtime support |
| --- | --- | --- |
| win32-x64 | win-x64 | Windows x64 |
| linux-x64 | linux-x64 | Linux x64, glibc |
| darwin-arm64 | osx-arm64 | macOS Apple Silicon |

Requires VS Code 1.137 or later and a trusted local workspace. Musl/Alpine, Intel macOS, remote virtual filesystems, and other architectures are not currently packaged. Remote VS Code sessions can run the extension on a supported workspace host.

## Development

Use Node.js 24+, npm, and a .NET SDK capable of building net10.0 with C# 14. From this folder:

```console
npm ci --ignore-scripts
npm run compile
npm test
npm run package -- --target win32-x64
```

Use `linux-x64` or `darwin-arm64` for the other packages. Packaging publishes the self-contained server, builds a platform-specific VSIX in `artifacts/`, and checks the ZIP contains the runtime, executable and client bundle. `PACKATA_VERSION` optionally supplies the shared repository release version; otherwise development packages use the manifest version. Packaging leaves the source manifest unchanged. Authentication dependencies of the packaging tool are pinned through npm overrides to avoid advisory-affected versions; none of the packaging dependencies ship in the VSIX.

Packaging invokes the SDK using its absolute installation path. Set `DOTNET_ROOT` for a nonstandard SDK location; otherwise the platform's standard installation directory is used. CI's .NET setup supplies this location. On Unix, packaging verifies that the published server is executable and is not writable by group or others; it does not change file permissions.

The solution includes `Packata.VSCode.csproj`, which installs locked npm dependencies when needed and compiles the client during `dotnet build Packata.sln`. The .NET projects separate shared document services (`Packata.Documents`), editor-independent document/tree services (`Packata.LanguageServer.Core`), and OmniSharp LSP handlers (`Packata.LanguageServer`).

To debug, open this folder in VS Code and press F5 using **Packata Extension**. First either copy the native server payload from `artifacts/staging-<target>/server` to `server/`, or set `packata.languageServer.path` in your development-host user settings to its absolute executable path. Attach the .NET debugger to the `Packata.LanguageServer` child process for server debugging. Do not commit machine paths.

From the repository root:

```console
dotnet test src/Packata.LanguageServer.Testing/Packata.LanguageServer.Testing.csproj -f net10.0
dotnet test src/Packata.Cli.Testing/Packata.Cli.Testing.csproj -f net10.0 --filter "FullyQualifiedName~InspectCommandTests|FullyQualifiedName~ValidateCommandTests"
```

`npm run test:host` exercises activation and unsaved-document navigation in a real Extension Development Host after compiling and packaging for the native target. Linux hosts need a display (CI uses `xvfb-run`). It downloads the matching VS Code test runtime into ignored artifacts and uses an isolated user profile.

## Communication and recovery

The thin TypeScript extension launches one persistent .NET server using `vscode-languageclient`. It exchanges LSP/JSON-RPC messages over stdio; it does not invoke the CLI for each action. The server calls the same Packata parsers, canonical mappers, and validators as the CLI.

Document synchronization includes unsaved text and versions. Tree refresh is debounced, with cancellation and stale-result rejection; stable node IDs preserve expansion when possible. In-memory analyses are reused for unchanged documents. Unopened descriptors are read from disk. Discovery skips `.git`, `.worktrees`, `node_modules`, `bin`, `obj`, `.vs`, symlink directories, inaccessible entries, and files larger than 4 MiB. Supported YAML/JSON descriptor content identifies contracts; unrelated YAML/JSON documents produce no tree or Packata diagnostics. Conventional `contract.yaml`/`datapackage.*` names retain a visible error entry when malformed.

Server communication failures clear the tree and display restart guidance. The language client performs bounded automatic restarts; **Packata: Restart Language Server** is also available. A new session reinitializes and resynchronizes open buffers before refreshing the tree. Requests have a configurable 30-second timeout (`packata.requestTimeout`); deactivation shuts down the child process. A missing or incompatible server produces installation/version guidance.

Protocol tracing is disabled by default. Avoid enabling verbose LSP tracing for documents containing secrets: traces include synchronized editor text. User-visible inspection/tree payloads are redacted, and parser/transport exceptions are replaced with fixed messages.

See [the protocol contract](../docs/language-server-protocol.md) for request/response shapes, diagnostics, version negotiation, and the preview extension boundary.

## Canonical and Native explorer views

Use **Packata: View as: Canonical / Native** from the command palette or the Data Contracts toolbar. Canonical is the default and shows assets, columns and constraints. Native shows the descriptor's original ODCS or Data Package v2 property names and hierarchy, including extension properties and empty values. The choice is saved per workspace and applies to every workspace folder.

Both projections come from the language server and follow unsaved edits. Switching preserves corresponding expanded/selected source nodes, with a nearest-parent fallback, and never rewrites the descriptor. Canonical also includes **Source properties (including unmapped)** so information outside the canonical mapping remains accessible. Mapping diagnostics appear on contract tooltips and in Problems/output; constraints, quality declarations and SLA commitments remain distinct. Credential values remain redacted in either view.
