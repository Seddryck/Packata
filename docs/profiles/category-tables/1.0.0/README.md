# Packata Category Tables profile 1.0.0

Canonical URI and `$id`:
https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/docs/profiles/category-tables/1.0.0/datapackage.json

This self-contained Draft 7 profile derives from the unmodified, embedded official
Data Package v2.0 schema (Unlicense). It follows the Category Tables recipe at
frictionlessdata/datapackage commit `3b9efb7b0fa08f08b0d55d2ba86b2be271423f94`.
It is a Packata compatibility profile, not an upstream standard profile.

The versioned Git tag publishes exactly the JSON embedded by Packata. Never move
that tag or change its published bytes; publish a new version/URI for changes.
No external `$ref` dependencies are needed. From the repository root, reproduce
with `node .github/scripts/Generate-CategoryTablesProfile.mjs`, then verify with
the same command plus `--check`. The generator asserts the expected base shapes
so upstream changes require review. The official artifact is never modified.

String/integer categories accept inline arrays or an object with required
`resource`, optional `package`, and optional `encodedAs` (`value` or `label`).
Resources may declare `categoryFieldMap` with required `value`, optional `label`
and boolean `ordered`; mapped resources must have a schema and be tabular.
Other extension metadata remains permitted as in the base profile.

Cross-resource existence, mapped field types, uniqueness and required constraints
are checked by Packata's semantic layer, not by this JSON Schema. No dataset rows
are read and external packages are not fetched during descriptor validation.
See `docs/datapackage-categories.md` for a complete example and runtime limits.
