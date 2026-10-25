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

The original `packata infer` spelling remains a compatibility alias with identical behavior.
Inference displays results by default. `--output` writes an enriched contract copy in the source
standard; `--write` atomically updates the source. They cannot be combined. CSV sampling options
remain `--maximum-records`, `--maximum-bytes`, `--culture`, `--header`, and `--no-dialect-inference`.
Use `packata schema infer --help` for their defaults. Bare subjects show help.

Database inference uses the shared connection service. It loads a compatible installed connector
or downloads the pinned connector from the bundled distribution catalog on first use, then continues in the same process. Acquisition
progress goes to stderr. No separate installation command or application code is required.
File-only inference does not read connector configuration or acquire database connectors.

## Command syntax

<!-- cli:connector list -->
### connector list

`packata connector list [--scheme <SCHEME>] [--verbose] [--output-format <FORMAT>]`

No positional arguments. `--scheme` optionally filters by database scheme (case-insensitive); default is all connectors. `--verbose` defaults off and expands text details. `--output-format` is `text` (default) or `json`; JSON is the full record array regardless of `--verbose`.

<!-- cli:connector show -->
### connector show

`packata connector show <ID> [--output-format <FORMAT>]`

Required canonical connector `<ID>` (case-insensitive); prints all matching version records with detailed text. `--output-format` is `text` (default) or `json`. Unknown IDs fail.

<!-- cli:connector install -->
### connector install

`packata connector install <ID>`

Required canonical catalog `<ID>`; installs/activates its pinned version, or reuses a healthy active installation. No application options; output is detailed text, progress/errors use stderr. Offline mode allows reuse but prevents a required download. There are no arbitrary download URL, version, force, update or uninstall options. Exit `0` succeeds, `1` is operation failure, `2` is caught cancellation or invocation error (framework cancellation can return `130`).

<!-- cli:config list -->
### config list

`packata config list [--output-format <FORMAT>]`

No positional arguments. `--output-format` is `text` (default) or `json`. Returns effective keys, values, origins and persisted user overrides where applicable.

<!-- cli:config get -->
### config get

`packata config get <KEY> [--output-format <FORMAT>]`

Required supported `<KEY>`. `--output-format` is `text` (default) or `json`; JSON is a one-record array, not a scalar.

<!-- cli:config set -->
### config set

`packata config set <KEY> <VALUE>`

Both positional arguments are required. No application options. Writes a user override atomically; relative store/catalog paths become absolute from the current directory. Quote paths containing spaces. Success has no result payload. Values cannot be empty; offline is Boolean, paths are local, preferences are canonical IDs.

<!-- cli:config unset -->
### config unset

`packata config unset <KEY>`

Required supported `<KEY>`; no application options. Removes the user override without deleting installed connector packs. Success has no result payload. A store environment override can remain effective after unsetting the user value.

<!-- end-cli -->
Config and discovery commands return `0` on success, `1` on operation/configuration failure, `2` on invalid invocation. Each command supports framework `--help`. Application options have no short aliases. JSON uses camelCase names. See [exit-code families]({{ '/cli/' | relative_url }}#sources-output-and-exit-code-families).

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

`list` and `show` inspect local catalog/store facts without loading modules, downloading archives,
or opening a database. JSON is a stable array of version records with `listed`, `installed`,
`active`, and `unhealthy` facts, diagnostic codes, framework/RID/API metadata, driver and prerequisites.
`loaded` is null because discovery does not load providers. `downloadAvailable` reports whether
the selected catalog version has distribution metadata. Installation alone does not establish
database connectivity. Installed versions absent from the catalog remain visible.

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

Settings are written atomically to `Packata/config.json` under the OS user application-data
directory. `PACKATA_CONFIG_HOME` selects another configuration directory, useful for isolated
CI jobs. No machine-wide configuration is changed. Store/catalog paths become absolute when set.
`DUBURL_PROVIDER_STORE` overrides the persisted store; otherwise user settings override defaults.
`config list/get` report effective origins and masked user values, with `--output-format text|json`.
`unset` removes a setting without deleting installed packs. Configuration never stores credentials.

The CLI embeds the schema-v2 `connector-catalog.json` published with
[DubUrl v0.46.0](https://github.com/Seddryck/DubUrl/releases/tag/v0.46.0), including immutable archive
URLs, SHA-256 hashes, and compatibility metadata. First-use downloads work without catalog setup.
The catalog entries are pinned with the CLI source. The original release download has SHA-256
`CB7F078BEA5299D077120FC718079977683A4768EE3A5D3E6E91495E9C8BF719`; it is not fetched or
silently upgraded at runtime. A future catalog update must review the package versions and archive
pins together. Hosts may still select another validated local distribution catalog explicitly.

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

Both options are repeatable. `--connector` overrides persisted preferences for this invocation.
`--connection-env` names a contract database endpoint and an environment variable containing its
runtime connection URL. It replaces connection details while preserving object namespace and asset
bindings. Duplicate or unknown endpoint overrides, missing variables, and malformed URLs fail
before acquisition. Runtime URLs are not written into the enriched contract or user configuration.

The invocation-owned database context also supplies reader factories using the same service and
runtime overrides. `packata data preview` uses this context for database endpoints, including the
same `--connector` and `--connection-env` options and configured offline policy. File previews keep
their existing readers and do not initialize database configuration. Provisioning remains tracked
separately in #476.
