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
or downloads the pinned connector from a configured distribution catalog on first use, then continues in the same process. Acquisition
progress goes to stderr. No separate installation command or application code is required.
File-only inference does not read connector configuration or acquire database connectors.

## Connector actions

```console
packata connector list
packata connector list --scheme postgresql --output-format json
packata connector show postgresql-npgsql
packata connector install postgresql-npgsql
```

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
| `connectors.catalog` | Bundled DubUrl 0.43.0 metadata catalog | Explicit local curated distribution catalog override, validated by DubUrl. |
| `connectors.preferred.<scheme>` | No preference | Resolve a scheme with multiple candidate technologies to a canonical connector ID. |

Settings are written atomically to `Packata/config.json` under the OS user application-data
directory. `PACKATA_CONFIG_HOME` selects another configuration directory, useful for isolated
CI jobs. No machine-wide configuration is changed. Store/catalog paths become absolute when set.
`DUBURL_PROVIDER_STORE` overrides the persisted store; otherwise user settings override defaults.
`config list/get` report effective origins and masked user values, with `--output-format text|json`.
`unset` removes a setting without deleting installed packs. Configuration never stores credentials.

DubUrl 0.43.0's bundled catalog contains connector descriptions but no archive URLs or hashes.
Until a compatible distribution catalog is shipped, downloads require configuring its local path
once with `packata config set connectors.catalog <path>`. The published DubUrl 0.40.0 catalog uses
an incompatible schema and cannot substitute for it. Missing distribution metadata produces an
explicit diagnostic; Packata does not invent URLs or bypass digest validation. Installed compatible
connectors remain usable with the default metadata catalog.

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
runtime overrides. Future preview/provisioning commands can reuse this context. Their command
surfaces remain tracked separately in #475 and #476; this change does not introduce those commands.
