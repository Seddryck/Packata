---
title: Quality execution and OORS reports
nav_order: 3
permalink: /cli-quality/
parent: Command-line interface
grand_parent: Reference
---

# Execute quality rules

```console
packata quality run contract.odcs.yaml
packata quality run contract.odcs.yaml --asset customers --field email --dimension completeness --dimension uniqueness
packata quality run contract.odcs.yaml --id customer-email-required --output report.oors.json
packata quality run contract.odcs.yaml --type library --output-format yaml --output report.oors.yaml
```

`quality run` loads and validates an ODCS v3.2.0 contract, selects its quality rules,
and dispatches them to the SQL or Expressif engine. The Expressif engine handles
ODCS library metrics and custom rules whose declared engine is `expressif`.
Text rules are reported as skipped. Unsupported capabilities accepted by an engine
are skipped; an unregistered rule type or invalid rule configuration is an error.
Incomplete measurements and execution timeouts are errors, never quality passes.

## Selection and execution

`--id`, `--asset`, `--field`, `--type`, and `--dimension` are optional and repeatable.
Values of one option use OR; different options use AND. Matching is exact and
case-sensitive. Field paths use dots for nested fields, and `--field` requires
`--asset`. Without filters, all asset and field quality rules are selected.

Only selected rules appear in the report. Filtered-out rules are absent, rather
than skipped. No matching rules is a command error with no report. Anonymous rules
can be selected through their other properties. Execution is sequential, and results
retain contract order. The runner delegates measurement and expectation evaluation
to each engine; it does not interpret SQL or Expressif programs.

Rules use the existing engine limits: 100,000 input rows, 100,000 state entries,
and a 30-second execution budget. Exhausting these limits prevents a reliable verdict.
File resources resolve relative to the contract, including supported archive sources.
An asset must have an unambiguous endpoint binding to read its data.

Database rules use the same connector configuration as other database commands.
`--connector SCHEME=ID` and `--connection-env ENDPOINT=VARIABLE` provide invocation
overrides without modifying the source contract. SQLite connections open with
`Mode=ReadOnly` and also enable `PRAGMA query_only = ON`. For other SQL providers, supply read-only database
credentials and `--sql-read-only-credentials` to assert that access is enforced by
those credentials. Without that guarantee, SQL execution reports an error.

A SQLite endpoint can be described using an ODCS custom server:

```yaml
servers:
  - server: local-db
    type: custom
    endpointUrl: sqlite://./warehouse.db?Pooling=false
    database: warehouse.db
    schema: main
    format: database
```

The authored SQL dialect is inferred from the server scheme, or the explicit
connection URL for a custom server. Use portable SQL when overriding an endpoint
to a different database at runtime.

## OORS export

OORS is the only export format. `--output-format json` (default) and `yaml` choose
serializations of the same OORS v0.8.0 document. There is no proprietary report or
text/table export. Every exported document is validated against a bundled copy of
the [OORS v0.8.0 schema](https://github.com/bitol-io/open-observability-results-standard/blob/56d16e6c650057dd5d71328d8256f5e790949d8d/schema/oors-json-schema-v0.8.0.json),
without a network request. OORS v0.8.0 is a draft standard; support is explicitly pinned.

The OORS envelope contains a unique document ID, observation time, process, run ID,
contract reference, and completed lifecycle state. A completed run can have failed
checks: lifecycle and quality outcome are distinct. Each result has a required ID,
name, observation type (`check`), status, available quality metadata, contract-element
reference, resource target, measurements/thresholds, and explanation.

Native contract IDs are used when unambiguous. Otherwise result IDs are derived
deterministically from contract paths, avoiding collisions. A generated result ID
does not imply that the contract had a rule ID.

Every Packata extension is an OORS `customProperties` entry with `vendor: packata`.
Look up an extension by both its vendor and property name.

| Location | Property | Value |
| --- | --- | --- |
| Document | `runTiming` | UTC start/finish and wall-clock duration with an explicit seconds unit |
| Document | `contractProvenance` | Available contract name, specification version, sanitized source, and deterministic SHA-256 fingerprint of the evaluated contract |
| Document | `runner` | Runner name/version |
| Document | `engines` | Participating engines, versions, handled rule types, and relevant runtime versions |
| Document | `filters` | Explicitly supplied filters, each represented as an array; absent without filters |
| Document | `summary` | Aggregate outcome and counts for pass/fail/warn/skip/error |
| Result | `ruleId` | Original contract rule ID, only when present |
| Result | `asset`, `field`, `ruleType`, `description` | Exact available contract context; rule type is distinct from OORS observation type |
| Result | `duration` | Attempted execution duration, omitted for unexecuted skips |
| Result | `output` | Observed output that cannot be represented faithfully by `measure.value` |

Numeric and string measurements use native `measure.value`. Booleans, objects and
arrays use the Packata `output` property and retain their types. Outputs are bounded
by a 4,096-character payload budget, a maximum nesting depth of six, and at most 32
entries per collection, with truncation markers. Secrets are redacted; raw evidence
records, SQL text, and provider exception messages are not included. URI credentials,
queries and fragments are removed from contract source locations.

The fingerprint uses a deterministic representation of the full contract descriptor
with sorted mapping keys and preserved sequence/scalar information. It changes when
the evaluated content changes even if the contract version does not.

Summary counts cover exactly the reported results. Aggregate precedence is
`error > fail > warn > pass > skip`. Warnings do not fail a quality gate. A skip
does not count as a pass or failure, and an error is distinct from a failed check.

| Exit code | Meaning |
| --- | --- |
| `0` | At least one pass or warning, with no failures or errors |
| `1` | At least one quality failure, with no execution errors |
| `2` | Command, contract, dispatch, execution, cancellation, or report error; no matches; or all selected rules skipped |

Reports are written to stdout or to a new file with `--output`; existing files are
never replaced. Diagnostics go to stderr. Completed rule failures/errors still
produce a report. Invalid contracts, invalid selection, cancellation before completion,
or report-production failures do not produce a successful report.
