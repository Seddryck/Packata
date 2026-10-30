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
results outdated. No dataset reads, quality execution or SLA compliance evaluation occurs.

See the [VS Code quickstart]({{ '/quickstart/vscode/' | relative_url }}) and
[language-server protocol](https://github.com/Seddryck/Packata/blob/main/docs/language-server-protocol.md).
