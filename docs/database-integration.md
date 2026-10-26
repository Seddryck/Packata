---
title: Database connections and provisioning
nav_order: 5
nav_exclude: false
permalink: /database-integration/
parent: Guides
---

# Connect to a database

To preview database records or infer a table's schema, Packata needs a contract
that identifies the table, a connector for the database, and connection settings.
The CLI can acquire connectors on first use. In a .NET application, you decide
which providers to include and how credentials reach the connection service.

This guide starts with the CLI workflow, then explains how to share a connection
service between .NET readers and provisioners. Creating tables is covered in the
[provisioning guide]({{ '/guides/provisioning/' | relative_url }}). CLI provisioning
is [planned](https://github.com/Seddryck/Packata/issues/476) and is not implemented.

## Find a CLI connector

List connectors for your database scheme. For PostgreSQL, run:

```console
packata connector list --scheme postgresql
```

The result lists connector IDs, versions and installation status. To see the
prerequisites and details for a particular connector, use its ID:

```console
packata connector show postgresql-npgsql
```

These commands inspect the catalog and local store. They do not download a
connector or connect to a database, so a listed connector does not establish that
your server is reachable.

When preview or inference first needs a database connector, the CLI reuses an
available provider or loads a compatible installed pack. If a download is needed,
it installs the version pinned in the bundled catalog and continues in the same
process. Progress appears on stderr. File-only operations do not acquire database
connectors.

## Supply the connection for a CLI command

Start with a local contract that already describes the database asset and its
endpoint. You can find their identifiers with `contract inspect`.

For the following example, assume `contract.yaml` is your ODCS descriptor, its
asset is named `customers`, and the database endpoint is named `warehouse`.
Your environment variable `WAREHOUSE_URL` must contain the runtime connection URL.
Then run:

```console
packata data preview contract.yaml --asset customers --limit 2 --connection-env warehouse=WAREHOUSE_URL
```

The command reads at most two records from the selected table. The endpoint name
before `=` identifies which connection to replace; the name after `=` identifies
the environment variable. The table binding and namespace still come from the
contract. The URL is not written to the descriptor or user settings.

To discover the table's schema instead of reading its records, use:

```console
packata schema infer contract.yaml --asset customers --connection-env warehouse=WAREHOUSE_URL
```

Inference reports database metadata without changing the descriptor by default.
Review its diagnostics because some databases or providers return partial metadata.
A missing environment variable, unknown or duplicate endpoint override, or malformed
URL fails before connector acquisition.

If several connectors support the database scheme, select one for this invocation
with `--connector postgresql=postgresql-npgsql`. Both `--connector` and
`--connection-env` can be repeated when needed. The
[database CLI reference]({{ '/cli-database/' | relative_url }}) lists all connector,
configuration and connection options; the
[inference reference]({{ '/cli-inference/' | relative_url }}) explains discovery options.

## Prepare for use without connector downloads

Install the required connector while downloads are available, then enable offline
mode in Packata's user configuration:

```console
packata connector install postgresql-npgsql
packata config set connectors.offline true
```

A healthy active installation is reused. Offline mode blocks connector downloads;
it does not block connections to your database. A missing connector produces an
error instead of being downloaded. Set `connectors.offline` back to `false` when
you want to permit downloads again.

The [configuration reference]({{ '/cli-database/' | relative_url }}#persistent-settings)
explains where these user settings are stored and how to choose a connector store
or a different local catalog. External ODBC and OLE DB vendor drivers remain
separate prerequisites.

## .NET: one service for readers and provisioners

Use `Packata.Database` when your application needs database readers, provisioners,
or both. A shared `DubUrlConnectionService` lets them use the same provider and
connection configuration. Each operation still opens its own connection.

The example below assumes your application implements `LoadRuntimeConfiguration`
to return `DatabaseRuntimeOptions` for an endpoint ID, for example from a secret
store. That function is application code, not a Packata API.

```csharp
using Packata.Database;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.Provisioners.Database;

using var connections = new DubUrlConnectionService(
    rootPath: AppContext.BaseDirectory,
    runtimeConfiguration: endpoint => LoadRuntimeConfiguration(endpoint.Id));
var readers = ResourceReaderFactory.Create(o => o.AddDatabase(connections));
var builder = new DubUrlProvisionerBuilder(connections, readers);
```

The constructor discovers provider dependencies included in your application.
Add the required `DubUrl.Provider.*` packages and concrete ADO.NET driver at build
time. This constructor does not configure an installed connector store.

The integration uses DubUrl 0.46.0 and its application connector service. Keep the
DubUrl package family on compatible versions; the older 1.1.0 publication does
not have these module and pack APIs. Core and the document-format packages do
not depend on DubUrl.

You can also use `AddDatabase()`, `AddDatabase(rootPath)` and parameterless
provisioner builders. The no-argument forms share `DubUrlConnectionService.Default`
for the life of the process. Callers must not dispose that shared instance. When
you create and supply a service yourself, your application owns it; reader
factories do not dispose it.

### Register providers explicitly

For control over provider selection, create `DatabaseConnectionServiceOptions`,
leave `DiscoverApplicationProviders` false, and add `IDubUrlProviderModule`
instances to `Modules`. Modules use DubUrl's public `Register` API to register
factories, aliases, dialects, parametrizers and connection compilers.

Explicit modules bypass assembly discovery. Attribute-based registration and
application or installed-pack discovery use reflection. Explicit registration
alone does not establish trimming or Native AOT support for the driver, dialect
or dependencies.

A duplicate alias throws `ProviderRegistrationException`, naming both conflicting
registrations. Separate services can use different factories for the same alias
without changing the application's global `DbProviderFactories`. For a fixed
configuration, you can supply a prebuilt `ISchemeRegistry` snapshot owned by your
application instead.

## Canonical endpoint and binding precedence

When configuring database endpoints in .NET, choose whether to supply a connection
URL or structured connection fields. A nonblank `ConnectionLocation.ConnectionUrl`
supplies the scheme, host, port, database, credentials and URL options. Conflicting
structured fields are ignored. `Namespace` is independent and still applies.

Without a URL, provide a valid `Scheme` and either a host or database. A database
without a host uses `.`. Ports must be between 1 and 65535, and IPv6 hosts are
bracketed. Structured database names are encoded as one path component; an explicit
URL retains its path segments.

The runtime callback can return `DatabaseRuntimeOptions` for each read. A complete
username and password replace URL credentials; a partial pair is rejected.
Runtime provider options override URL options without regard to key casing.
Duplicate keys within either source are rejected. Resolve secrets by endpoint ID
so credentials do not have to be stored in contract documents.

Provisioner builders take destination credentials through `WithCredentials`.
They do not invoke the reader's endpoint callback. Advanced callers can pass
runtime options directly to `Resolve`.

### Select the table and namespace

The reader selects the table from `DataEndpointReadRequest.AssetPath` first,
then from the format's `table` option. An empty selection fails before a connector
is acquired or a connection opened. Choose a binding explicitly when an asset
has more than one.

For qualification, `ConnectionLocation.Namespace` takes precedence over the
format's `namespace` option. The dialect renders and escapes the namespace and
table separately. A period inside either value is part of that identifier;
use the namespace field to qualify a table. SQLite uses double-quoted identifiers
rather than DubUrl's unquoted formatter.

User information, structured database names and provider options are encoded
before the URL is used. DubUrl 0.46.0 decodes URI components when parsing; Packata
does not decode them a second time. File and inline readers follow their own
paths and do not invoke database connector discovery or acquisition.

## Installed packs and first-read acquisition

If your .NET application should load or acquire connector packs at runtime,
configure a store and a catalog explicitly. This example assumes that your
application has supplied a trusted, curated `connector-catalog.json`:

```csharp
using DubUrl.Core.ProviderPacks;

using var catalogFile = File.OpenRead("connector-catalog.json");
var catalog = ProviderPackCatalog.Read(catalogFile);
using var http = new HttpClient();
var options = new DatabaseConnectionServiceOptions
{
    RootPath = AppContext.BaseDirectory,
    ProviderStore = new ProviderPackStore("application-connectors"),
    Catalog = catalog,
    HttpClient = http,
    DownloadsEnabled = true
};
// If several technologies match without a curated default alias:
// options.ConnectorPreferences["postgresql"] = "postgresql-npgsql";
using var connections = new DubUrlConnectionService(options);
var readers = ResourceReaderFactory.Create(o => o.AddDatabase(connections));
// await readers.OpenAsync(new DataEndpointReadRequest(endpoint, schema, binding.AssetPath), token);
```

The first read can acquire a missing connector and continue without rebuilding
the reader factory or restarting the application. Construction, contract mapping
and `Inspect` do not download anything.

`ProviderStore` chooses an explicit store. Alternatively, set
`UseDefaultProviderStore = true` to use DubUrl's default user store, honoring
`DUBURL_PROVIDER_STORE`. Do not set both. A store can load active local packs
without a catalog. To run without downloads, activate a compatible connector
first, then set `DownloadsEnabled` to false. Inactive packs are not loaded.

### What happens when a database reader opens

1. Packata reuses a loaded provider for the requested scheme if one is available.
2. Otherwise, it checks for changes in the installed store and loads active,
   compatible packs through DubUrl's `InstalledProviderModuleLoader`.
3. If a connector still needs to be selected, an explicit canonical ID is exact.
   Otherwise, application preferences take precedence, followed by a unique
   catalog alias, then a sole scheme match. Unresolved choices produce
   `connector-ambiguous`; invalid preferences are diagnosed. A connector ID
   covering several databases still needs the actual database scheme to open one.
4. If acquisition is enabled, Packata calls
   `DubUrl.Application.IConnectorService.InstallAsync` with the canonical ID.
   DubUrl checks the pinned version, archive SHA-256, safe extraction and
   API/TFM/RID compatibility, then installs and activates the pack atomically.
5. Packata publishes a fresh registry snapshot and opens the pending connection.

Concurrent reads that need the same connector coordinate installation and
publication. Existing URLs and readers retain their snapshots and connections.
Loaded assemblies are not replaced: changing a loaded connector requires a new
service, catalog or process.

Installation verifies the archive digest. Later store inspection checks metadata,
compatibility and required assets without rehashing every installed file. Invalid
binaries or missing runtime dependencies produce loading diagnostics; one unhealthy
pack does not hide valid packs. Conflicts between explicit and installed modules
fail rather than silently replacing registrations.

An application can inject `IConnectorService` to control installation. With a
custom `IDatabaseConnectionService`, it can also pass
`IConnectorAvailabilityService` to `AddDatabase`; the returned runtime scheme is
used to open the connection.

Your application supplies and trusts its catalog. Contracts cannot supply
arbitrary connector download URLs. ODBC and OLE DB vendor drivers are still
separate prerequisites, and authentication, permission or connectivity failures
do not trigger connector reinstallation.

## Discovery and diagnostics

Call `connections.Inspect("postgresql")` to check catalog entries, installed
versions and loaded providers without opening a connection or using the network.
Each `ConnectorInfo` reports `Listed`, `Installed`, `Active` and `Unhealthy`
independently, including versions installed locally but absent from the catalog.

`Loaded` means a runtime provider is available. It does not establish server
connectivity, schema-deployment support or bulk-loading support. Connector IDs
and aliases resolve through the catalog; IDs covering several databases can be
used for discovery, but opening a connection requires the actual database scheme.

Use diagnostic codes to distinguish a missing installation from a selection or
loading problem. Availability codes include `connector-loaded`,
`connector-unknown`, `connector-not-installed`, `connector-inactive`,
`connector-unhealthy` and `connector-not-loaded`. Selection and loading may also
report `connector-ambiguous`, `connector-preference-invalid`, `connector-offline`
or `connector-load-failed`.

Installation diagnostics can include DubUrl's `pack-integrity`, `api-incompatible`,
`tfm-incompatible`, `rid-incompatible` and `connector-download-failed` codes.
Messages identify the connector and missing prerequisite.
`DatabaseConnectionException` exposes `Code` and, where available, `EndpointId`
and `ConnectorId`.

Provider and store exception details are omitted because they can contain secrets.
Advanced `ConnectionUrl.Url` and `Parse()` values contain connection material and
should not be logged. Runtime options redact their `ToString()` representation.

## Ownership and cancellation

Sharing the connection service shares configuration. Each operation gets a new
URL, mapper and connection. If you call `OpenAsync` directly, you own the
`Connection` in the returned `DatabaseConnectionSession`. A database reader owns
its reader, command and connection until you close or dispose it.

Failed connection opening, connection-string assignment or query execution
releases resources owned by that operation. Disposal attempts every release even
if another disposal throws. Dispose your service after its readers have finished.

The service owns its installed-module loaders and any HTTP client it creates.
Modules, registries, connector services and HTTP clients supplied by your
application remain yours to dispose. Old loader contexts remain available while
readers may need their snapshots and are released when the service is disposed.

Cancellation passes through installation, opening and asynchronous query execution.
Synchronous providers check around blocking calls; Packata cannot interrupt a
driver that does not cooperate. Cancellation remains an `OperationCanceledException`
with the caller's token, including DubUrl's derived connector-cancellation type.
Upstream installation preserves store consistency when cancelled.

Packata does not implicitly install machine-wide drivers or provide an uninstall
interface or background management loop.

## Provisioning operation preflight

Call `DubUrlProvisioner.GetOperationSupport()` before choosing a provisioning
operation. `Reading`, `SchemaDeployment` and `BulkLoading` are independent:
a provider that can query a database may not be able to deploy or bulk-load it.

Schema deployment requires SQL Server, DuckDB, PostgreSQL, SQLite, MySQL or Oracle
dialect support, or a registered `DatabaseSchemaDeploymentSupport` capability.
An ANSI fallback or arbitrary custom dialect does not establish support.

Bulk loading requires the actual `ConnectionUrl.BulkCopyProvider` capability.
A dialect name or catalog entry is insufficient. Native DuckDB and SQL Server
connector packs include typed bulk adapters. A build-time provider without an
adapter can still support schema-only deployment.

For other adapters, an application can supply `IDatabaseOperationCapabilities`
to the builder or provisioner. Before SQL runs or source data is read, `ExecuteAsync`
checks deployment and loading support, cancellation and reader configuration.
Use `DeploySchema` when you only need schema deployment.

Unsupported operations throw `DatabaseOperationNotSupportedException`, identifying
the operation and provider dialect. Failed resolution uses
`database-preflight-failed`. These checks establish known capability, not server
permissions, deployment success or transactional rollback. The
[provisioning guide]({{ '/guides/provisioning/' | relative_url }}) shows how to
review a plan before deploying it.

## Examples and validation scope

From a repository checkout, run the self-contained database example:

```console
dotnet run --project examples/DatabaseConnections --framework net10.0
```

It creates a temporary DuckDB database, inserts a customer record and reads it
through both ODCS and Data Package mappings. Each read prints `Customer ID: 42`.
The example uses provider dependencies included at build time and removes the
temporary database when it finishes.

To try connector acquisition, supply your own trusted distribution catalog and
choose a store directory:

```console
dotnet run --project examples/ConnectorAcquisition --framework net10.0 -- connector-catalog.json application-connectors
```

Its first ODCS read loads or acquires DuckDB, then the Data Package read reuses
the provider snapshot. The paths in the command are inputs you must supply;
this example can download connectors when the catalog and store require it.

The repository's acquisition tests use real provider assemblies, isolated
temporary stores and an in-memory HTTP handler. They exercise installation,
loading and embedded DuckDB queries without public downloads or machine-wide
driver installation. `Packata.Database` is a portable managed package targeting
.NET 8, 9 and 10; concrete drivers remain application dependencies. The existing
Windows tests exercise native SQLite and DuckDB loading, which does not by itself
establish support for every driver on every operating system.
