---
title: Schema inference
parent: Command-line interface
grand_parent: Reference
nav_order: 1
nav_exclude: false
permalink: /cli-inference/
---

<!-- cli:schema infer -->
<!-- cli:infer -->
## schema infer / infer

```text
packata schema infer <SOURCE> --asset <ID> [OPTIONS]
packata infer <SOURCE> --asset <ID> [OPTIONS]
```

`infer` is a compatibility alias with identical settings and behavior. `<SOURCE>` is a **local** Data Package v2 JSON/YAML or ODCS YAML descriptor with `.json`, `.yaml` or `.yml` extension. ZIP/remote descriptors are not supported by this loader. Select one exact asset with exactly one endpoint binding. CSV inference requires a single CSV/TSV/PSV file path; ODCS CSV endpoints must be local files. Database inference reads metadata for the selected physical object using the shared database service; partial discoveries produce diagnostics.

| Option | Values / default | Meaning |
| --- | --- | --- |
| `--asset <ID>` | Required | Exact canonical asset identifier |
| `--output <PATH>` | Omitted | Write an enriched copy to a **new** file; missing parent directories are created |
| `--write` | Off | Atomically replace the source with its enriched form; mutually exclusive with `--output` |
| `--maximum-records <COUNT>` | Positive integer; `1000` | Maximum sampled CSV records; ignored for databases |
| `--maximum-bytes <COUNT>` | Positive integer; unlimited when omitted | Optional sampled CSV byte limit; ignored for databases |
| `--culture <NAME>` | Invariant culture when omitted/empty | Culture for inferred CSV values, e.g. `fr-FR`; ignored for databases |
| `--header <MODE>` | `auto`, `present`, `absent`; `auto` | CSV header mode; auto honors a declared header hint; ignored for databases |
| `--no-dialect-inference` | Off | Use contract dialect hints without inferring a CSV dialect; ignored for databases |
| `--connector <SCHEME=ID>` | Repeatable; persisted preferences otherwise | Invocation-only connector preference |
| `--connection-env <ENDPOINT=VARIABLE>` | Repeatable; no override by default | Runtime database URL from a named environment variable; not persisted |

The CSV-only options still undergo syntax/value validation on database invocations. No `--format`, JSON presentation switch, `--overwrite`, or short option aliases are implemented for inference. See [database override precedence]({{ '/cli-database/' | relative_url }}#invocation-overrides).

```console
packata schema infer datapackage.json --asset customers --header present --maximum-records 100
packata infer datapackage.json --asset customers --maximum-bytes 1048576 --culture en-US
packata schema infer datapackage.json --asset customers --no-dialect-inference --output enriched.json
packata schema infer contract.yaml --asset customers --connector postgresql=postgresql-npgsql --connection-env warehouse=WAREHOUSE_URL
```

## Output and mutation

Inference prints a human-readable report of mapping/inference decisions, confidence where available, the selected schema and dialect. It has no machine-readable output contract. Without `--output` or `--write` it does not write the descriptor. `--output` refuses an existing file. `--write` is the explicit destructive choice for updating the source; there is no overwrite prompt.

Updating enriches the selected resource/schema and endpoint metadata rather than producing an arbitrary new standard. Review emitted preservation warnings: JSON/YAML are reserialized, comments/formatting and YAML anchors are not a round-trip guarantee, and unsupported source constructs can limit an update. Runtime connection overrides are not written into the enriched contract. Relative output paths resolve from the working directory; data paths resolve using the contract's resource context. File inference errors can include source-specific messages; redact sensitive details before sharing logs.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Inference completed |
| `1` | Inference, input, write or database operation failed |
| `2` | Inference caught cancellation; also CLI invocation/settings errors |
| `130` | Framework-level cancellation when handled before/around command execution |

These are the legacy inference semantics, not the `0/1/2/3/130` contract of validation/inspection/export/preview/diff. CSV inference is synchronous inside its command path and may not stop mid-sample. See [read/infer task guide]({{ '/guides/data/' | relative_url }}).
