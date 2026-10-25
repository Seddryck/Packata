---
title: Troubleshooting
parent: Project
nav_order: 1
nav_exclude: false
permalink: /troubleshooting/
---

| Symptom | Check |
| --- | --- |
| Command/option rejected | `packata --version` and command `--help`; check the [CLI reference]({{ '/cli/' | relative_url }}) for that command's option names |
| Asset/endpoint not found or ambiguous | Inspect the contract; use exact canonical IDs and select one binding |
| Relative file not found | Keep sample CSV beside its descriptor; resolve library paths against the descriptor directory |
| Recognized format missing provider | Install/register the package suggested by `ReaderProviderUnavailableException`; check CLI versus library availability |
| Unsupported format | Check explicit format/media type/extension and [reader configuration]({{ '/guides/readers-storage/' | relative_url }}) |
| Database connector missing/offline | `connector list`, `connector show ID`, `config list`; preinstall compatible connectors before offline mode |
| Database cannot connect after installation | Check runtime credentials, network, permissions and external driver prerequisites; installation is not a connectivity check |
| Export cannot overwrite a file | Choose a new target; only dialect export supports explicit `--overwrite`, and neither export may overwrite its source |
| Diff reports changes but returns zero | Expected: factual differences are a successful result, not a compatibility failure |

Preserve diagnostic codes and command version when [reporting an issue]({{ '/contact/' | relative_url }}). Redact connection strings and credentials; do not include raw secret-bearing URLs. Cancellation and failures use command-specific exit contracts, especially legacy inference and connector installation.
