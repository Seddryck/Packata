---
title: VS Code Quickstart
parent: Getting started
nav_order: 3
nav_exclude: false
permalink: /quickstart/vscode/
---

Install a Packata Language Support VSIX for your platform using **Extensions:
Install from VSIX**, then open a trusted folder with an ODCS 3.2.0 or Data Package v2
YAML/JSON descriptor. The package bundles its server and runtime; see the
[extension installation instructions](https://github.com/Seddryck/Packata/tree/main/vscode-extension).

## Validate a descriptor

1. Open the descriptor or select it in **Packata → Data Contracts**.
2. Run **Packata: Validate…** from the Command Palette or file context menu.
3. Select **Schema**, **Semantic**, or both, then press Enter.
4. Read categorized findings in Problems and per-check outcomes in **Packata Validation**.

Schema validates the descriptor, not dataset rows. A schema-only success says
**Schema checks passed · Semantic not run**. The [semantic checklist]({{ '/standards/validation/' | relative_url }})
explains the second layer. Neither layer reads dataset contents.

The picker remembers confirmed choices per workspace and format across restarts.
Escape preserves them. **Packata: Run Last Validation**, or the editor status-bar
action with its scope tooltip, repeats them. Edits mark results outdated; rerunning
replaces their diagnostics. See the [VS Code reference]({{ '/reference/vscode/' | relative_url }})
for blocked checks, cancellation and execution errors.

To preview data, use the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}).
Editor data preview remains tracked separately in [#451](https://github.com/Seddryck/Packata/issues/451).
