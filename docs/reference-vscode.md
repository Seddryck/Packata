---
title: VS Code reference
parent: Reference
nav_order: 3
nav_exclude: false
permalink: /reference/vscode/
---

Packata Language Support uses a bundled server in a trusted local workspace.
Install a platform-specific VSIX as described in the [extension README](https://github.com/Seddryck/Packata/tree/main/vscode-extension).

| Command | Behavior |
| --- | --- |
| **Packata: Validate…** | Choose Schema, Semantic or both for the active or right-clicked YAML/JSON descriptor. |
| **Packata: Run Last Validation** | Repeat this workspace's confirmed scope for the document format, or open the picker if none is available. |
| **Packata: Inspect Contract** | Inspect the current buffer without certifying validation. |
| **Packata: Preview Resource Data…** | Select a file resource and a positive maximum number of data rows (default 20). |
| **Packata: Refresh Contracts** | Rediscover contracts across workspace folders. |
| **Packata: Restart Language Server** | Restart and resynchronize open buffers. |

Both checks start selected. Choices persist per workspace and format across restarts.
Empty selection cannot run; Escape leaves saved choices unchanged. The editor
status-bar action exposes the remembered scope in its tooltip. New validators never
silently enter existing selections; unavailable saved checks require confirmation.

Schema checks concern descriptor structure, not dataset records. Semantic checks
cover the [documented declaration rules]({{ '/standards/validation/' | relative_url }}).
The **Packata Validation** output identifies the file and each check's outcome.
Schema-only success says **Schema checks passed · Semantic not run**. Syntax errors
block checks; selected schema failure blocks selected semantics. Semantic-only does
not enable schema. Cancellation and execution errors report incomplete validation.

Problems use `Packata · Schema`, `Packata · Semantic`, or `Packata · Syntax` categories
with source navigation where available. Reruns replace prior diagnostics; edits mark
results outdated. Validation does not read datasets or execute quality rules or SLA evaluations.

## Resource previews

Open an ODCS or Data Package JSON/YAML descriptor and run **Packata: Preview Resource Data…**,
or right-click a supported asset in either Packata tree projection. Choose the resource
binding when prompted, then enter a positive row limit; the default is **20**. A table
shows column names, declared or detected types, and up to that many data rows. Headers
do not count. Loading is cancellable; access, unsupported-format and parsing failures
produce sanitized messages. Source edits during selection require a new selection.

Preview supports **CSV, TSV, PSV and Excel (XLS/XLSX)** using Packata's existing readers
and descriptor dialect settings. Only local files and HTTP/HTTPS file URLs are eligible.
Public and pre-signed URLs use the existing HTTP transport without custom authentication
headers or interactive login. Database endpoints, native cloud-storage schemes and
declared custom authentication requirements have no active tree preview action.
Other formats, including Parquet (whose reader requires full row-group decoding), are
currently unavailable for bounded editor preview.

Relative references resolve against the original descriptor location, including unsaved
buffers. Ambiguous or missing endpoint bindings produce a diagnostic; Packata never
silently chooses another server. Inspection, discovery and tree refresh read metadata
only; resource access starts with an explicit preview request after row-limit selection.

The limit bounds **data rows read**, not transferred bytes. Readers may buffer input;
Excel may require metadata reads or temporary disk staging, bounded to 256 MiB for
non-seekable inputs. Readers, streams and staging files are released on completion,
failure or cancellation. Dataset values appear in the table; diagnostics omit source
excerpts, credentials and sensitive URL components.

See the [VS Code quickstart]({{ '/quickstart/vscode/' | relative_url }}) and
[language-server protocol](https://github.com/Seddryck/Packata/blob/main/docs/language-server-protocol.md).
