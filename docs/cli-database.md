---
title: Database commands and configuration
nav_order: 2
nav_exclude: false
permalink: /cli-database/
parent: Command-line interface
grand_parent: Reference
---

# Database commands and user configuration

Infer a bound resource with the canonical subject/action command:

```console
packata schema infer contract.yaml --asset customers
packata schema infer datapackage.json --asset customers --output enriched.json
packata schema infer contract.yaml --asset customers --write
```

The original `packata infer` spelling remains an alias with identical behavior. Inference
displays its report without changing the source by default. Use `--output` for a new enriched
copy, or `--write` to update the source atomically; the two options cannot be combined.

For CSV sampling, use `--maximum-records`, `--maximum-bytes`, `--culture`, `--header` and
`--no-dialect-inference`. The defaults are shown by `packata schema infer --help`.
Invoking a command group without an action also shows help.

Database inference uses the shared connection service. On first use, it loads a compatible
installed connector or downloads the pinned version from the bundled catalog, then continues
in the same process. Acquisition progress goes to stderr.

You do not need a separate installation step or application code for this path. File-only
inference does not read connector configuration or acquire a database connector.

## Command syntax

<!-- cli:connector list -->
### connector list

`packata connector list [--scheme <SCHEME>] [--verbose] [--output-format <FORMAT>] [--output <PATH>]`

With no filter, list shows every connector. Add `--scheme` to filter by database scheme;
the comparison is case-insensitive. There are no positional arguments.

Text is the default presentation. Choose JSON with `--output-format` when you need the full
record array. The optional `--verbose` switch expands text details but does not change JSON.

To save this result, pass a new destination to `--output <PATH>` or its alias, `-o <PATH>`.
Omit the option to write to stdout. The command finishes rendering before writing, protects
existing files and omits ANSI styling from file output. A file-write failure returns `1`.

<!-- cli:connector show -->
### connector show

`packata connector show <ID> [--output-format <FORMAT>] [--output <PATH>]`

Supply the connector's canonical `<ID>`; the lookup is case-insensitive. Show returns all
matching versions, with detailed text by default, and fails if the ID is unknown.
Use `--output-format` to choose text or JSON.

To save this result, pass a new destination to `--output <PATH>` or its alias, `-o <PATH>`.
Omit the option to write to stdout. The command finishes rendering before writing, protects
existing files and omits ANSI styling from file output. A file-write failure returns `1`.

<!-- cli:connector install -->
### connector install

`packata connector install <ID>`

Supply a canonical catalog `<ID>` to install and activate its pinned connector version.
A healthy active installation is reused. Offline mode allows that reuse but prevents a
download when one is needed.

Install has no application options. It prints detailed text, with progress and errors on
stderr. It does not accept arbitrary download URLs, versions, force, update or uninstall options.

Success returns `0` and an operation failure returns `1`. A caught cancellation or invalid
invocation returns `2`; framework-level cancellation can return `130`.

<!-- cli:config list -->
### config list

`packata config list [--output-format <FORMAT>] [--output <PATH>]`

List returns the effective configuration keys, values and origins, including persisted user
overrides where applicable. It takes no positional arguments. Text is the default; use
`--output-format` to request JSON.

To save this result, pass a new destination to `--output <PATH>` or its alias, `-o <PATH>`.
Omit the option to write to stdout. The command finishes rendering before writing, protects
existing files and omits ANSI styling from file output. A file-write failure returns `1`.

<!-- cli:config get -->
### config get

`packata config get <KEY> [--output-format <FORMAT>] [--output <PATH>]`

Supply one supported `<KEY>` to inspect its effective value. Get uses text by default;
choose JSON with `--output-format`. JSON is a one-record array rather than a scalar value.

To save this result, pass a new destination to `--output <PATH>` or its alias, `-o <PATH>`.
Omit the option to write to stdout. The command finishes rendering before writing, protects
existing files and omits ANSI styling from file output. A file-write failure returns `1`.

<!-- cli:config set -->
### config set

`packata config set <KEY> <VALUE>`

Supply both the configuration key and its value. Set writes the user override atomically
and has no application options or success payload. Store and catalog paths become absolute
from the current directory, so quote paths containing spaces.

Values cannot be empty. The offline setting takes a Boolean, store and catalog settings take
local paths, and preferences take canonical connector IDs.

<!-- cli:config unset -->
### config unset

`packata config unset <KEY>`

Supply the supported `<KEY>` whose user override you want to remove. Unset has no application
options or success payload, and does not delete installed connector packs.

An environment override for the store can still take effect after the user value is unset.

<!-- end-cli -->
Config and discovery commands return `0` on success, `1` on operation/configuration failure, `2` on invalid invocation. Each command supports framework `--help`. Presentation commands accept `-o` as the alias for `--output`; no other short application aliases are defined here. JSON uses camelCase names. See [exit-code families]({{ '/cli/' | relative_url }}#exit-code-families).

## Connector actions

```console
packata connector list
packata connector list --verbose
packata connector list --scheme postgresql --output-format json
packata connector show postgresql-npgsql
packata connector install postgresql-npgsql
```

`list` displays a compact table of connector IDs, versions, status and download availability,
with the current runtime and counts of installed and active connector versions. Unhealthy
status takes precedence over active or installed status; diagnostic codes appear beneath the
affected row. Shared discovery and connectivity caveats appear once.

Use `list --verbose` for per-connector discovery facts, aliases, API/framework/runtime metadata,
driver and dependency notes. Missing runtime metadata is shown as `Not specified`, which does
not imply compatibility with every runtime. `show` and installation results use this detailed
layout. Text output remains readable without color and when redirected.

List and show inspect the catalog and local store without loading provider modules,
downloading archives or opening a database. Installed versions remain visible even when
they are absent from the catalog. Discovery does not establish database connectivity.

JSON is a stable array of version records. Each record carries `listed`, `installed`,
`active` and `unhealthy` facts, diagnostic codes, framework/RID/API metadata, driver details
and prerequisites. The `loaded` value is null because discovery does not load providers.
The `downloadAvailable` value indicates whether that catalog version has distribution metadata.

`install` requires a canonical ID and installs/activates the curated version. A healthy active
installation is reused without downloading. Integrity, compatibility, extraction and activation
are handled by DubUrl. ODBC/OLE DB vendor drivers remain separate prerequisites. There are no
update/uninstall actions or arbitrary package URL inputs in this command group.

## Persistent settings

```console
packata config list
packata config get connectors.offline --output-format json
packata config set connectors.offline true
packata config set connectors.store D:/packata/connectors
packata config set connectors.catalog D:/packata/catalog.json
packata config set connectors.preferred.postgresql postgresql-npgsql
packata config unset connectors.store
```

| Key | Default | Meaning |
| --- | --- | --- |
| `connectors.offline` | `false` | Disable connector downloads, including explicit installation; available connectors and database networking still work. |
| `connectors.store` | DubUrl's OS-appropriate per-user store | Directory for verified installed connector packs. |
| `connectors.catalog` | Bundled DubUrl 0.46.0 distribution catalog | Optional local curated distribution catalog override, validated by DubUrl. |
| `connectors.preferred.<scheme>` | No preference | Resolve a scheme with multiple candidate technologies to a canonical connector ID. |

User settings are written atomically to `Packata/config.json` under the operating system's
application-data directory. Set `PACKATA_CONFIG_HOME` to use a different directory, for
example in an isolated CI job. These commands do not change machine-wide configuration
or store credentials.

Store and catalog paths become absolute when set. `DUBURL_PROVIDER_STORE` takes precedence
over the saved store path; otherwise, user settings override defaults. The list and get
commands report effective origins and masked user values in text or JSON. Unsetting a value
removes the override without deleting installed packs.

The CLI bundles the schema-v2 `connector-catalog.json` published with
[DubUrl v0.46.0](https://github.com/Seddryck/DubUrl/releases/tag/v0.46.0). It pins archive URLs,
SHA-256 hashes and compatibility metadata, so first-use downloads need no catalog setup.

The catalog itself is not fetched or silently upgraded at runtime. The original release
download has SHA-256 `CB7F078BEA5299D077120FC718079977683A4768EE3A5D3E6E91495E9C8BF719`.
Future catalog updates must review package versions and archive pins together. Hosts can
still choose another validated local distribution catalog explicitly.

Prepare an offline job by installing its connector before enabling offline mode:

```console
packata connector install postgresql-npgsql
packata config set connectors.offline true
packata schema infer contract.yaml --asset customers
```

Set `connectors.offline` back to `false` to permit downloads. Missing offline connectors produce
an actionable error. Store, catalog and offline policy are configured once; individual commands
have no repeated store/catalog/offline flags.

## Invocation overrides

```console
packata schema infer contract.yaml --asset customers --connector postgresql=postgresql-npgsql
packata schema infer contract.yaml --asset customers --connection-env warehouse=WAREHOUSE_URL
```

Both options can be repeated. Use `--connector` to override a persisted connector preference
for this invocation. Use `--connection-env` to name a database endpoint and the environment
variable holding its runtime connection URL.

The URL replaces connection details while retaining the object's namespace and asset bindings.
Duplicate or unknown endpoint overrides, missing variables and malformed URLs fail before
acquisition. Runtime URLs are not saved in the enriched contract or user configuration.

The invocation-owned database context also supplies reader factories using the same service and
runtime overrides. `packata data preview` uses this context for database endpoints, including the
same `--connector` and `--connection-env` options and configured offline policy. File previews keep
their existing readers and do not initialize database configuration. Provisioning remains tracked
separately in #476.
