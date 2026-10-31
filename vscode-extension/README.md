# Packata Language Support

Navigate ODCS v3.2 and Frictionless Data Package v2 contracts in the **Packata → Data Contracts** Activity Bar view. The native tree discovers descriptors across workspace folders, displays canonical assets and columns, and preserves format-specific metadata. Context menus provide **Open Source**, **Inspect Contract**, **Validate…**, and **Copy Logical Path**. Click a source-backed item to reveal its definition.

Open buffers take precedence over saved descriptors, including unsaved edits. Constraints, quality declarations, and SLA commitments remain separate. Source quality declarations are labelled as such; the extension does not execute quality rules. No empty quality/SLA sections are invented. Mapping diagnostics remain visible even when some source information cannot be represented canonically; use Open Source to inspect the complete descriptor.

Inspection is distinct from validation. A successful tree load does not display a passing validation status. Explicit validation reports through VS Code Problems, using the same server diagnostic collection as automatic document checks. **Packata: Preview Resource Data…** explicitly reads a bounded sample from supported file resources.

## Select validation checks

Run **Packata: Validate…** on the active editor, a file context menu, or a contract
tree entry. The native multi-select picker identifies the file and format. Select
**Schema**, **Semantic**, or both; an empty selection cannot run. Schema checks
the descriptor's structure and types, not dataset records. Semantic checks declaration
consistency, references and supported contract rules.

Both checks start selected. Confirmed choices survive restarts per workspace and
format. Escape runs nothing and preserves the saved scope. **Packata: Run Last
Validation** and the editor status-bar action repeat that scope; its tooltip shows,
for example, **Validate: Schema + Semantic**. Missing or unavailable saved choices
require confirmation. New validators never enter remembered selections implicitly.

Problems identify `Packata · Schema`, `Packata · Semantic`, or `Packata · Syntax`,
with source navigation where available. **Packata Validation** output shows the
file and every check's outcome: passed, errors, not run, blocked, cancelled or
execution error. A schema-only success says **Schema checks passed · Semantic not
run**. Parsing blocks checks; selected schema failure blocks selected semantics;
semantic-only never enables schema. Background checks validate syntax only.
Completed reruns replace previous diagnostics; edits mark results outdated.

## Install the extension

Install the VSIX for your platform with **Extensions: Install from VSIX**. Each package bundles its own .NET server and runtime; a separate .NET or Packata CLI installation is unnecessary.

| VSIX target | Server RID | Runtime support |
| --- | --- | --- |
| win32-x64 | win-x64 | Windows x64 |
| linux-x64 | linux-x64 | Linux x64, glibc |
| darwin-arm64 | osx-arm64 | macOS Apple Silicon |

Requires VS Code 1.137 or later and a trusted local workspace. Musl/Alpine, Intel macOS, remote virtual filesystems, and other architectures are not currently packaged. Remote VS Code sessions can run the extension on a supported workspace host.

## Resource preview

Run **Packata: Preview Resource Data…** from an open descriptor, or right-click a
supported asset in either tree projection. Select the resource binding and a positive
maximum number of data rows, default **20**. Column names, declared/detected types
and rows appear in a table. Headers are excluded from the limit. Loading supports
cancellation, and failures show sanitized diagnostics.

Supported formats are CSV, TSV, PSV and Excel (XLS/XLSX), using the existing Packata
readers and descriptor dialect settings. Resources must be local files or HTTP/HTTPS
file URLs. Public and pre-signed URLs use the existing transport without custom
authentication headers or interactive login. Database endpoints, native cloud schemes
and declared custom authentication requirements have no active tree preview action.
Parquet and other formats are currently unsupported for bounded editor preview.
Ambiguous bindings require a diagnostic or explicit selection; relative paths resolve
against the original descriptor, including unsaved buffers.

The selected limit bounds data rows read, not bytes transferred. Input buffering,
format metadata and Excel disk staging may still be necessary. Non-seekable staging
is bounded to 256 MiB. Completion, failures and cancellation release owned readers,
streams and temporary files. Discovery, inspection and refresh never read resource data.

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

The solution includes `Packata.VSCode.csproj`, which installs locked npm dependencies when needed and compiles the client during `dotnet build Packata.sln`. The .NET projects separate shared application workflows (`Packata.Application`), editor-independent document/tree services (`Packata.LanguageServer.Core`), and OmniSharp LSP handlers (`Packata.LanguageServer`).

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

## Generate schema code

Run **Packata: Generate Schema Code…** from the Command Palette or a contract/asset context
menu in either explorer projection. Select an eligible asset when prompted, then **C#
record**, **DataTable**, **Polars.NET**, **Arrow Schema (C#)**, **Arrow Schema (Python)**, or **Custom template…**. The catalog comes from
Application through the server, so its targets, engines and mapping profiles match CLI.
Use **Generate** for defaults or customize namespace/type name. Custom templates allow an
engine override, optional mapping profile and repeatable user variables. Recognized engine
suffixes retain their meaning; otherwise Scriban is the default, including `.template.cs`.

Generation runs under cancellable progress and opens complete output in an editable
**untitled** document named for the generated type (C# or Python for the selected target/language). Inspect it and use
normal **Save As**. Generation never saves, overwrites or edits the descriptor/template.
Existing output files require normal editor confirmation when explicitly saved. Source
and template paths are excluded from automatic output filename association.

Current unsaved descriptor and open-template contents are captured for each request. A
source/template edit, changed selection, server restart or cancellation prevents obsolete
output from opening. For multiple roots, the template picker uses the source descriptor's
owning folder. Unsupported mappings and template errors are actionable findings; **View
findings** navigates only to actual supplied ranges. Generation findings do not replace
contract validation Problems. Didot rendering itself is synchronous; cancellation discards
its result after rendering completes.

Requiredness, value nullability, categories and target representation have distinct meaning.
DataTable and Polars schemas do not enforce every contract constraint, and category codes
are never replaced automatically by labels/enums. See the repository's
[shared generation reference](https://seddryck.github.io/Packata/reference/dotnet/schema-generation/)
for context version 1, examples and target limits. The feature is disabled in untrusted or
virtual workspaces and reports missing generation capabilities on older servers. It reads
no rows, uses no database connections, and never compiles or executes generated output.
