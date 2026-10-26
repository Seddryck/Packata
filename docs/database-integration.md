---
title: Database connections and provisioning
nav_order: 5
nav_exclude: false
permalink: /database-integration/
parent: Guides
---

# Database connections, connectors and provisioning

Use `Packata.Database` to share a DubUrl connection service between database readers and
provisioners. Core and the document-format packages remain independent of DubUrl.

The integration uses published DubUrl **0.46.0**, including its application connector service.
Keep the DubUrl package family on compatible versions: the older 1.1.0 publication lacks
these module and pack APIs. The earlier shared-service milestone, `cbf64b6`, was compiled
against 0.29.34 before the modular API upgrade.

## CLI

Start with `packata connector list` and `packata connector show ID` to check the connector
and its prerequisites. You can then use `schema infer` or `data preview` with the contract's endpoint.
To supply a runtime URL, use `--connection-env ENDPOINT=VARIABLE` and keep secrets in runtime
configuration rather than the contract.

The [connector reference]({{ '/cli-database/' | relative_url }}) lists every configuration option,
and the [inference reference]({{ '/cli-inference/' | relative_url }}) covers schema discovery.
CLI provisioning remains [planned](https://github.com/Seddryck/Packata/issues/476).

## .NET: one service for readers and provisioners

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
// LoadRuntimeConfiguration is host code returning DatabaseRuntimeOptions from a secret store.
```

The convenience constructor discovers the application's provider dependencies once. It does
not configure an installed provider store, so add the required `DubUrl.Provider.*` packages
and concrete ADO.NET driver at build time.

You can still use `AddDatabase()`, `AddDatabase(rootPath)` and parameterless provisioner
builders. The no-argument entry points share `DubUrlConnectionService.Default`, which lasts
for the process and must not be disposed by callers.

When you supply a service explicitly, your host owns it; reader factories do not dispose it.
Packata does not register providers globally for the application.

To choose providers explicitly at build time, create `DatabaseConnectionServiceOptions`,
leave `DiscoverApplicationProviders` false, and add `IDubUrlProviderModule` instances to
`Modules`. Each module registers its providers through DubUrl's public `Register` API,
including factories, aliases, dialects, parametrizers and connection compilers.

Readers and provisioners use that same configuration. A duplicate alias throws
`ProviderRegistrationException` and names both conflicting registrations. Separate services
can use different factories for the same alias without interfering through `DbProviderFactories`.
For a fixed configuration, you can instead pass a prebuilt, host-owned `ISchemeRegistry` snapshot.

Explicit module registration bypasses assembly discovery. Attribute-based registration and
application or installed-pack discovery use reflection. Choosing explicit registration alone
does not establish trimming or Native AOT support for the driver, dialect or its dependencies.

## Canonical endpoint and binding precedence

When a nonblank `ConnectionLocation.ConnectionUrl` is present, it supplies the scheme, host,
port, database, credentials and URL options. Conflicting structured fields are ignored, but
`Namespace` remains independent of the URL.

Without a URL, supply a valid `Scheme` and either a host or database. A database without a
host uses `.`. Ports must be between 1 and 65535, and IPv6 hosts are bracketed. A structured
database name is encoded as one path component; an explicit URL keeps its existing path segments.

For each read, the runtime callback can supply `DatabaseRuntimeOptions`. A complete username
and password replace URL credentials; a partial pair is rejected. Runtime provider options
override URL options case-insensitively, and duplicate keys within either source are rejected.
Resolve secrets by endpoint ID so they do not enter canonical documents.

Provisioner builders accept destination credentials through `WithCredentials`. They do not
invoke the reader's endpoint callback. Advanced callers can also supply runtime options
directly to `Resolve`.

To select a table, the reader uses `DataEndpointReadRequest.AssetPath` before the format's
`table` option. Empty selections fail before connector acquisition or connection opening.
If an asset has multiple endpoint bindings, choose one explicitly; the reader does not guess.

For qualification, `ConnectionLocation.Namespace` takes precedence over the format's
`namespace` option. The dialect renders namespace and table separately and escapes closing
delimiters. A period inside either value is a literal identifier character, so use the namespace
field to qualify a table. SQLite uses double-quoted identifiers rather than DubUrl's unquoted formatter.

User info, structured database names and provider options are encoded safely. DubUrl 0.46.0 decodes
URI components at parsing; Packata does not decode a second time. File and inline readers retain
their dispatch and never invoke database connector availability or acquisition.

## Installed packs and first-read acquisition

```csharp
using DubUrl.Core.ProviderPacks;

using var catalogFile = File.OpenRead("connector-catalog.json");
var catalog = ProviderPackCatalog.Read(catalogFile); // Authenticated, curated host configuration.
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

Installed packs participate only when you configure a store. Set `ProviderStore` to choose
one, or opt into DubUrl's default user store with `UseDefaultProviderStore = true`. The latter
also honors `DUBURL_PROVIDER_STORE`. Do not combine the two settings.

A store can load active local packs without a catalog. Construction, mapping and `Inspect`
do not download connectors. To work offline, activate a compatible connector first, then set
`DownloadsEnabled` to false. This blocks downloads while allowing active compatible packs;
inactive packs are not loaded.

A database `OpenAsync` follows this order:

1. Reuse a loaded provider for the requested scheme.
2. Observe changed installed-pack facts and load active, compatible packs through DubUrl's
   `InstalledProviderModuleLoader`, preserving upstream manifest and compatibility checks.
3. Select a host-curated connector. Explicit canonical IDs are exact selections. Otherwise host
   preferences take precedence, then a unique catalog alias, then a sole scheme match. Multiple
   unresolved technologies produce `connector-ambiguous`; an invalid preference is diagnosed.
   A connector ID covering multiple databases requires the actual database scheme when opening.
4. If acquisition is enabled, call `DubUrl.Application.IConnectorService.InstallAsync` with the
   canonical connector ID. The application service and store enforce pinned versions, archive
   SHA-256, safe extraction, API/TFM/RID compatibility and atomic installation/activation.
5. Build and atomically publish a fresh registry snapshot before opening the pending connection.
   The original factory remains usable; callers do not rebuild it or restart the process.

When several reads need the same missing connector, they coordinate its installation and
publication. Existing URLs and readers retain their registry snapshots and live connections.
Loaded assemblies are not replaced, so changing a loaded connector requires a new service,
catalog or process rather than hot replacement.

Installation verifies the archive digest. Later store inspection checks metadata, compatibility
and required assets without rehashing every installed file. Invalid binaries or missing runtime
dependencies produce loading diagnostics, but one unhealthy pack does not hide other valid packs.
Conflicts between explicit and installed modules fail rather than replacing registrations silently.

A host can inject `IConnectorService` through the options to control installation. With a
custom `IDatabaseConnectionService`, it can also pass `IConnectorAvailabilityService` to
`AddDatabase`. The selected runtime scheme from the availability result is used to open
the connection.

The host is responsible for trusting and delivering its catalog. Contracts cannot provide
arbitrary connector download URLs, and external ODBC or OLE DB vendor drivers remain separate
prerequisites. Authentication, permission and connectivity failures do not trigger reinstallation.

## Discovery and diagnostics

Call `connections.Inspect("postgresql")` to inspect catalog metadata, installed-store facts
and the loaded registry without opening a connection or using the network. Each `ConnectorInfo`
has independent `Listed`, `Installed`, `Active` and `Unhealthy` facts for its version, including
versions installed locally but absent from the catalog.

The `Loaded` fact means that a runtime provider is available. It does not establish server
connectivity, schema-deployment support or bulk-loading support. Connector IDs and aliases
resolve through the catalog. An ID covering multiple databases is useful for discovery,
but opening a connection still requires the actual database scheme.

Availability diagnostics use stable codes such as `connector-loaded`, `connector-unknown`,
`connector-not-installed`, `connector-inactive`, `connector-unhealthy` and
`connector-not-loaded`. Selection and loading can also report `connector-ambiguous`,
`connector-preference-invalid`, `connector-offline` or `connector-load-failed`.

Installation can return upstream codes including `pack-integrity`, `api-incompatible`,
`tfm-incompatible`, `rid-incompatible` and `connector-download-failed`. Messages identify
the affected connector and missing prerequisite. `DatabaseConnectionException` exposes
`Code` and, when available, `EndpointId` and `ConnectorId`.

Provider and store exception details are omitted because they can contain secrets. Do not log
advanced `ConnectionUrl.Url` or `Parse()` values: these contain connection material rather
than safe diagnostics. Runtime options redact their `ToString()` representation.

## Ownership and cancellation

Each operation gets a new URL, mapper and connection. Sharing the service shares configuration,
not a live connection. A caller of `OpenAsync` owns the `Connection` in the returned
`DatabaseConnectionSession`. A database reader owns its reader, command and connection until
you call `Close` or `Dispose`.

If opening, connection-string assignment or query execution fails, the operation releases its
owned resources. Disposal attempts every release even if another disposal throws.

Cancellation passes through installation, opening and asynchronous query execution.
Synchronous providers check it around blocking calls; Packata cannot interrupt a driver that
does not cooperate. Cancellation remains `OperationCanceledException`, including DubUrl's
derived connector-cancellation type, with the caller's token. Upstream installation preserves
store consistency when cancelled.

Dispose the service after its readers. It owns its installed-module loaders and any HTTP
client it creates internally. Modules, registries, connector services and HTTP clients supplied
by the host remain host-owned.

Old loader contexts stay available while readers may still use their snapshots, and are released
when the service is disposed. Packata does not implicitly install machine-wide drivers or
provide an uninstall interface or background management loop.

## Provisioning operation preflight

Call `DubUrlProvisioner.GetOperationSupport()` before choosing an operation. Its `Reading`,
`SchemaDeployment` and `BulkLoading` facts are independent. Reading means generic database querying.

Schema deployment requires a supported dialect: SQL Server, DuckDB, PostgreSQL, SQLite, MySQL
or Oracle, or a host-registered `DatabaseSchemaDeploymentSupport` capability. An ANSI fallback
or arbitrary custom dialect does not establish support.

Bulk loading depends on the actual `ConnectionUrl.BulkCopyProvider` capability, not a dialect
name or catalog entry. Native DuckDB and SQL Server connector packs include typed bulk adapters.
A build-time provider without its adapter can still be used for schema-only deployment.

When configuring other adapters deliberately, a host can supply `IDatabaseOperationCapabilities`
to the builder or provisioner. Before DDL or source reads, `ExecuteAsync` checks deployment
and loading support, cancellation and reader configuration. You can still use `DeploySchema`
without bulk loading.

An unsupported operation throws `DatabaseOperationNotSupportedException` naming the operation
and provider dialect. Failed resolution uses `database-preflight-failed`. Preflight establishes
known capability; it does not guarantee permissions, deployment success or transactional rollback.

## Examples and validation scope

The build-time example reads the same database through mapped ODCS and Data Package resources:

```console
dotnet run --project examples/DatabaseConnections --framework net10.0
```

The acquisition example uses a host-supplied curated distribution catalog and explicit store.
Its first ODCS read loads or acquires DuckDB; the Data Package read reuses that snapshot:

```console
dotnet run --project examples/ConnectorAcquisition --framework net10.0 -- connector-catalog.json application-connectors
```

The controlled acquisition tests use real provider assemblies, isolated temporary stores and an
in-memory HTTP handler. They exercise actual upstream installation/loading and query embedded
DuckDB in the same process; they do not download from public servers or install machine-wide drivers.

| Dimension | Repository evidence and decision |
|---|---|
| Deliverable | One portable managed `Packata.Database` NuGet package; concrete drivers remain host dependencies |
| Frameworks | Inherited .NET 8, 9 and 10; compile all three and run affected tests on each |
| Test OS | Existing Windows library runner; SQLite/DuckDB tests exercise native driver loading there |
| Distribution | Existing version strategy and publishing destinations; no new executable, RID, architecture or libc variants |
| Release | Add the package to build-output upload, no-build packaging and artifact validation; preserve release permissions and triggers |
