# Packata language-server protocol

Packata uses the same host separation as Expressif.LanguageServer: a TypeScript `vscode-languageclient`, a persistent .NET OmniSharp language server, and services that call Packata libraries directly. The CLI and LanguageServer independently host `Packata.Application`, which replaces `Packata.Documents` and coordinates descriptor inspection and validation.

`ContractInspector.Inspect` accepts descriptor text or reusable `ContractSyntax`, format options, a base URI and cancellation. It returns the native document, source syntax, canonical mapping and diagnostics before host presentation. `Validation.DescriptorValidation.Validate` runs the selected offline validation layers and retains syntax for diagnostic locations. Neither workflow resolves credentials, acquires connectors or reads referenced resource data. `ContractJson` owns redaction and JSON projection without exposing mutable serializer settings.

Pass `options: new ContractInspectionOptions(AssetId: "orders")` to inspect one asset and its bound endpoints. Application performs the projection while retaining the complete source document and original mapping diagnostics. `DescriptorFormat` provides shared standard identification for inspection, validation, editor menus and native-tree fallback, including partial documents. Validation layers expose structured prerequisite status; editor adapters translate it into labels such as `Blocked`.

`DataPreviewService` coordinates explicit preview through an injected `IDataEndpointReaderFactory`. Both hosts share binding resolution, typed column projection, bounded sampling, null handling, cancellation and reader disposal. Reader factories supply each host's capabilities: CLI supports archives, databases and category decoding; LanguageServer retains its permitted file formats, authentication restrictions and stale-selection checks. Preview target discovery and inspection never open resource data.

The CLI owns argument parsing, local/HTTP/archive acquisition, output destinations and exit codes. The LanguageServer owns synchronized document versions, discovery, caching, source ranges, trees and LSP publication. Domain libraries retain their models, mapping and validation rules; neither host references the other.

Application services also cover schema/dialect inference, descriptor updates, standalone path normalization, schema/dialect export, contract diff/compatibility/satisfaction and quality execution/OORS reporting. These APIs accept explicit inputs and cancellation; file inference uses an injected `IInferenceStreamSource`, database inference receives a resolved connection, and quality execution receives reader and SQL connection capabilities. Export and update operations return content and diagnostics without choosing destinations or writing files. Config/connector management, acquisition lifetimes, read-only database policy and the interactive asset workflow remain CLI responsibilities; document synchronization, workspace discovery, source navigation and protocol negotiation remain LanguageServer responsibilities. The feature audit and scope are recorded in [issue #681](https://github.com/Seddryck/Packata/issues/681).

Application validates inference options against the resolved file format before opening streams. Non-default option values are checked automatically. Set `ContractInferenceOptions.SuppliedOptions` when an explicitly supplied value equals its default (for example, `Header=auto`); CLI translates flags into this semantic metadata. Unsupported combinations produce `UnsupportedInferenceOptionsException`, whose option identifiers can be rendered by any host.

| Application API | Responsibility |
| --- | --- |
| `ContractInferenceService`, `DialectInferenceService` | CSV/Excel/Parquet/database inference, dialect sampling and save policies |
| `DescriptorUpdateService`, `StandaloneContractService` | Metadata-preserving descriptor updates and standalone locator normalization |
| `ContractExportService` | Canonical mapping, output selection and JSON Schema/Table Dialect content |
| `ContractComparisonService` | Native validation/provenance, diff, compatibility and producer/consumer satisfaction |
| `QualityExecutionService`, `QualityReportService` | Rule preparation/filtering, engine composition, execution and offline-validated OORS reports |

## Session and capabilities

The client launches the server without a shell, then sends standard LSP `initialize` and `initialized` messages. The server advertises full text synchronization and execute-command support. Its command list includes `packata.protocol.v1`, `packata.workspace.discover`, `packata.contract.inspect`, and `packata.contract.validate`. The versioned capability command identifies Packata payload version 1 independently of LSP's version. Clients must verify that command and all operations they use are advertised, request `schemaVersion: 1`, and reject response versions they do not understand.

All operations below use `workspace/executeCommand` with one typed argument. Method/command names are extension protocol contracts, not CLI commands. A capability query with `command: "packata.protocol.v1"` and `arguments: [{"schemaVersion":1,"workspaceFolders":[]}]` returns `{"schemaVersion":1,"schemaVersions":[1],"positionEncoding":"utf-16"}`.

LSP positions use zero-based lines and UTF-16 code-unit columns (the standard default, also used by .NET and VS Code). No alternative encoding is advertised. Stdin/stdout carry only LSP messages with `Content-Length` framing. Logs must not be written to stdout. The server disables its default exception logger so source fragments and paths cannot leak through logging; the client displays fixed transport errors. User-enabled verbose protocol traces necessarily contain document contents and should be treated as sensitive.

Shutdown follows the LSP `shutdown` request / `exit` notification sequence. The language client restarts crashed processes with a bounded retry policy; manual restart remains available. Each restart initializes a new session and reopens synchronized documents before discovery. No persistent document state is trusted across processes.

## Documents and source locations

Use `textDocument/didOpen` with URI, version and full contents, then `didChange` with increasing versions and full replacement contents, and `didClose` when the buffer is released. Older or equal versions are ignored. A synchronized buffer takes precedence over its saved file. On close, diagnostics are cleared and later inspection reads disk again. Save notifications are not needed because changes already carry current text. Workspace folders are supplied to discovery; the VS Code client triggers discovery after workspace-folder and watched-file changes.

Every inspection/validation result identifies its URI and document version (`null` for disk-only descriptors). Relative resource references keep the original descriptor's base URI in the shared document context, even when the text is unsaved. Neither discovery, inspection, nor validation opens resource data. Explicit preview requests reuse this context and the same connection.

Canonical node logical paths and native source paths have different purposes. Tree nodes carry stable IDs, canonical logical paths and optional `location: {uri,range}` values derived from actual YAML/JSON syntax tokens. A logical path alone is not a location. Collection ranges may end at their last represented token rather than a closing delimiter. Nodes without a reliable mapping remain inspectable and do not receive a fabricated reveal command. Clients discard stale replies and refuse navigation using a location from an older open-document version.

## Discovery

```json
{
  "jsonrpc": "2.0", "id": 1, "method": "workspace/executeCommand",
  "params": {"command":"packata.workspace.discover","arguments":[{"schemaVersion":1,"workspaceFolders":["file:///workspace/a/","file:///workspace/b/"]}]}
}
```

The result is `{schemaVersion:1, contracts: Inspection[]}`. Discovery considers candidate YAML/JSON files in every supplied local folder and synchronized open buffers. Packata content recognition filters unrelated documents. Malformed conventional descriptor filenames remain recognized error entries. Symlink directories, generated/dependency folders, and descriptors over 4 MiB are excluded from scanning. Unsupported URI schemes for unopened files result in request errors; no remote fetching happens implicitly.

## Inspection

```json
{"jsonrpc":"2.0","id":2,"method":"workspace/executeCommand","params":{"command":"packata.contract.inspect","arguments":[{"schemaVersion":1,"uri":"file:///workspace/contract.yaml"}]}}
```

An inspection result has this shape (contract contents omitted here for readability):

```json
{
  "schemaVersion":1,
  "uri":"file:///workspace/contract.yaml",
  "version":3,
  "baseUri":"file:///workspace/",
  "recognized":true,
  "format":"odcs",
  "contract":{},
  "diagnostics":[],
  "tree":{
    "id":"file:///workspace/contract.yaml#/",
    "label":"Customer Data", "kind":"contract", "logicalPath":"/",
    "description":"ODCS", "location":null, "children":[]
  }
}
```

`contract` is the shared canonical `Packata.Core.Contracts.DataContract` representation, projected as camel-case JSON with string enums and credential redaction. The server-generated tree provides editor-independent grouping and source mappings; it is not a second contract parser. Real results include the populated canonical contract and tree. A node has `id`, `label`, `kind`, `logicalPath`, optional `description`/`location`, and `children`. Asset IDs are name-based (occurrence suffixes disambiguate duplicates); fields include their occurrence index. Consumers should treat IDs as opaque.

Inspection includes mapping diagnostics and does not certify successful validation. Unsupported or malformed recognized documents have no canonical contract/tree and include a safe diagnostic. Well-formed source can still provide the native projection described below. Unrelated documents return `recognized:false` with no diagnostics.

## Resource preview

Inspection includes metadata-only `previewTargets`: `{sourcePath,bindingIndex,label,
supported,reason,selectionKey}`. Match `sourcePath` to an asset node in either tree
projection. Only supported targets receive an active preview action. `selectionKey`
is an opaque fingerprint; return it to reject changed descriptors rather than silently
previewing another resource. Missing or ambiguous bindings remain diagnostic targets.

Send `packata.resource.preview` with `{schemaVersion:1,uri,sourcePath,limit:20,
bindingIndex:0,selectionKey}` after the user selects a positive integer limit. Omitted
`limit` defaults to 20. Invalid limits fail before descriptor or resource access.
The response is `{schemaVersion:1,uri,version,limit,columns:[{name,type}],rows,
diagnostic}`. A diagnostic-bearing result has empty columns and rows and a fixed,
sanitized message; it never includes an exception dump or resource URL. Headers do
not count toward the row limit. No extra row is read to detect truncation.

CSV/TSV/PSV and XLS/XLSX file endpoints support local paths and public/pre-signed
HTTP/HTTPS URLs through the existing transport. Custom authentication headers,
interactive authentication, database endpoints and native cloud schemes are
unsupported and rejected before resource access. Parquet is unavailable because
its current reader decodes full row groups. Unsupported formats return diagnostics.
Relative paths resolve against the original descriptor directory or HTTP base URI.
Cancellation propagates into acquisition and reads; owned streams/readers and any
Excel staging files are disposed on all exit paths. Row limits do not bound bytes:
buffering, metadata reads and bounded disk staging can still occur.

## Validation and diagnostics

Send `packata.validation.options` with the inspection argument to obtain
`{schemaVersion:1,format,validators}`. Each validator has a stable `id`, `label`,
`description`, applicable `formats` and `blockedBySelected` IDs. The catalog drives
the native multi-select workflow; new entries never expand saved selections.

Send `packata.contract.validate` with `{schemaVersion:1,uri,checks:["schema"]}` to
execute exactly that selection. Omitted `checks` preserves the explicit default
`["schema","semantic"]`; empty or unknown selections are rejected. The response is
`{schemaVersion:1,uri,version,format,valid,diagnostics,checks}`. Every catalog check
has `id`, `label`, `state`, error count and optional reason. States include `Passed`,
`Failed`, `NotSelected` and `Blocked`. Parsing blocks selected checks; failed selected
schema checks block selected semantics. Semantic-only never adds schema. `valid`
covers selected checks only, so clients must display their scope.

The handler replaces previous scopes through `textDocument/publishDiagnostics`.
Automatic checks run syntax only after a 180 ms document-change debounce, without
replacing current explicit results for the same version. The client marks edits
outdated, discards superseded replies, and distinguishes cancellation and execution
errors. It uses the single LSP collection rather than duplicating Problems entries.

A diagnostic DTO has `code`, `severity` (`Error`, `Warning`, `Information`), `sourcePath`, `message`, optional `range`, and validation `layer` (`Schema`, `Semantic`, `Syntax`). Paths resolve to syntax-backed ranges, falling back to a containing declaration for absent fields. LSP uses source `Packata · <layer>` (plain `Packata` for inspection), and `{sourcePath,locationUnavailable}` in `data`. Unmapped diagnostics use an empty document-start range with `locationUnavailable:true`; this is a document-level fallback, not a claimed precise source location.

Mapping warnings, schema constraints, quality declarations, and SLA commitments retain their separate meanings. Source quality metadata is labelled as a declaration with no execution support. The tree only displays passing validation status after a current explicit validation result, and invalidates that status on edits/file changes or reconnect.

## Errors and cancellation

The server returns JSON-RPC error `-32602` for an unsupported Packata payload version and `-32001` for inaccessible/unsupported document URIs or operation failures. Error messages are fixed and contain no exception dump or source excerpt. Invalid descriptor contents are normal diagnostic-bearing results. LSP cancellation uses `$/cancelRequest`; completion may race cancellation, so callers must also discard replies for superseded work. The extension imposes its configured request timeout and sends cancellation when it expires.

## Packaging decisions

| Dimension | Evidence and decision |
| --- | --- |
| Deliverables | Portable shared services, a self-contained .NET executable, and platform-specific VSIX packages; no new public NuGet packages |
| TFM | Libraries/shared CLI document services retain net8.0/net9.0/net10.0; server and client build integration use net10.0 |
| Native targets | Expressif reference distributions: win-x64, linux-x64 (glibc), osx-arm64; matching native CI runners smoke-test each |
| Excluded targets | No additional x86, Windows ARM, Intel macOS or musl distribution promise in this milestone |
| Versions | Existing CI calculates one release version; VSIX and bundled server use that value; local development defaults to 0.1.0 |
| Prerequisites | Locked npm dependencies, Node 24, .NET 10 SDK; existing icon reused, no new asset generator |
| Validation | Focused service/protocol/client tests, Extension Development Host smoke test, ZIP content/runtime checks; cross-publishing alone does not establish native execution |
| Publication | Existing main-branch release flow attaches validated VSIX artifacts; PR builds only validate/package; no Marketplace publishing or new credentials |

## Explorer projections

Inspection and discovery now also return `nativeTree` (an additive version-1 field). `tree` remains the canonical projection and includes a source-properties branch for content the canonical mapping does not represent. `nativeTree` preserves every source mapping property and sequence item, including unknown properties, false/null values and empty collections; credential-bearing values remain redacted. The original property names provide ODCS schema/properties/servers/SLA/team and Data Package resources/path/dialect/schema/fields/contributors hierarchies without a second client parser.

Both projections carry optional `sourcePath` JSON Pointers and syntax-derived `location` values. `logicalPath` is canonical in canonical nodes and the native JSON Pointer in native nodes. Stable opaque IDs use unique source names for named sequence items, falling back to occurrence indices for unnamed/duplicate items. Clients match source paths across projections, then fall back to the closest corresponding parent. Well-formed source that fails native deserialization can still return `nativeTree` with diagnostics and a null canonical tree; malformed, cyclic, oversized or excessively deep syntax provides diagnostics without a fabricated tree.

The extension stores its Canonical/Native preference in VS Code workspace state, shared by all workspace folders. Switching uses the already synchronized inspection snapshot; ordinary refresh/version/cancellation handling updates both projections together. Source navigation reinspects the current buffer and searches both trees for the selected opaque ID.
