# Pinned descriptor schemas

These files are embedded verbatim. Validation never downloads schemas.

| File | Source | SHA-256 |
| --- | --- | --- |
| `odcs-v3.2.0.json` | `bitol-io/open-data-contract-standard`, release commit `f0bdad95346905d500be5ef4b2c2d9b1d95223b7`, `schema/odcs-json-schema-v3.2.0.json` | `edb41f33ec46e84780e99872ab2bd67f074959d2bf3e9c9fc54e61f8982b0d93` |
| `datapackage.json` | `https://datapackage.org/profiles/2.0/datapackage.json`, retrieved 2026-10-30 | `a9ef0fc168b3402ae7aa7d22bbcb798e0db6b639e7ee15ff4aa177463cea7112` |

ODCS is Copyright The Linux Foundation and contributors, Apache-2.0
([upstream license](https://github.com/bitol-io/open-data-contract-standard/blob/f0bdad95346905d500be5ef4b2c2d9b1d95223b7/LICENSE)).
Data Package is from Frictionless Data / Open Knowledge Foundation, dedicated to
the public domain under the Unlicense
([upstream license](https://github.com/frictionlessdata/datapackage/blob/da3526c4ed19563620d6a5e33ea55db6234cc411/LICENSE.md)).

When updating ODCS, run `pwsh .github/scripts/export-odcs-validation-inventory.ps1`
to regenerate `docs/odcs-schema-assertions.tsv` from schema objects (including
definitions and applicator branches, excluding examples and defaults). It records
every assertion, reference and applicator occurrence. Update the version policy, hashes, discrepancy matrix and
regressions together; do not silently replace the schema with a newer `latest`.
