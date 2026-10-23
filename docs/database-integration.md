# Database connections, connectors and provisioning

`Packata.Database` supplies the shared DubUrl service used by database readers and provisioners.
Core and the document-format packages have no DubUrl dependency. The completed integration uses
published DubUrl **0.43.0**, including its application connector service. Keep the DubUrl package
family on compatible versions: the older 1.1.0 publication does not expose these module/pack APIs.
The initial shared-service milestone (`cbf64b6`) was separately compiled against 0.29.34 before
upgrading to the modular API for the subsequent integration steps.

## One service for readers and provisioners

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

The convenience constructor discovers application provider dependencies once, with no installed
provider store. Add the required `DubUrl.Provider.*` family and concrete ADO.NET driver at build
time. Existing `AddDatabase()`, `AddDatabase(rootPath)` and parameterless provisioner builders
remain available; the no-argument entry points share `DubUrlConnectionService.Default`. Do not
dispose that process-lifetime default. The host owns an explicitly supplied service; factories
do not dispose it. No Packata path calls application-global provider registration.

For explicit build-time selection, create `DatabaseConnectionServiceOptions`, leave
`DiscoverApplicationProviders` false, and add `IDubUrlProviderModule` instances to `Modules`.
Each module's `Register` method adds `ProviderRegistration` objects through DubUrl's public API,
including factory delegates, aliases, dialects, parametrizers and connection compilers. The same
selected configuration serves readers and provisioners. Duplicate aliases throw
`ProviderRegistrationException` with both conflicting registration names. Independent services
can use different factory delegates for the same alias without `DbProviderFactories` interference.
A prebuilt `ISchemeRegistry` can also be passed to the service for a fixed, host-owned snapshot.

Explicit module registration bypasses assembly discovery. Attribute-based modules and application
or installed-pack discovery use reflection; explicit registration alone does not establish
trimming or Native AOT support for a driver, dialect or dependency closure.

## Canonical endpoint and binding precedence

1. A nonblank `ConnectionLocation.ConnectionUrl` is authoritative for scheme, host, port,
   database, URL credentials and URL options. Conflicting structured connection fields are ignored.
   `Namespace` remains independent of the URL.
2. Without a URL, a valid `Scheme` and either a host or database are required. A database without
   a host uses `.`. Ports must be between 1 and 65535. IPv6 hosts are bracketed. A structured database
   name is encoded as one path component; explicit URLs preserve their existing path segmentation.
3. The runtime callback supplies `DatabaseRuntimeOptions` for each database read. A complete
   username/password pair replaces URL credentials; partial pairs are rejected. Runtime provider
   options override URL options case-insensitively. Duplicate keys within either source are rejected.
   Resolve secrets by endpoint ID without placing them in canonical documents. Provisioner builders
   accept destination credentials through `WithCredentials`; they do not invoke the reader's
   endpoint callback. Advanced callers can pass runtime options to `Resolve` directly.
4. `DataEndpointReadRequest.AssetPath` overrides the `table` format option. Empty selections fail
   before connector acquisition or connection opening. Select one binding explicitly when an asset
   has multiple endpoint bindings. The reader does not guess a table. `ConnectionLocation.Namespace`
   overrides the `namespace` format option. Namespace and table are rendered separately using the
   resolved dialect, with closing delimiters escaped. Periods within either value are literal
   identifier characters; use the namespace field for qualification. SQLite uses its supported
   double-quoted syntax rather than the unquoted formatter supplied by DubUrl.

User info, structured database names and provider options are encoded safely. DubUrl 0.43.0 decodes
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

Only an explicitly configured `ProviderStore` participates. `UseDefaultProviderStore = true` is a
separate opt-in to DubUrl's default user store (including its `DUBURL_PROVIDER_STORE` override);
do not combine both settings. Store-only configuration loads active local packs without requiring
a catalog. Construction, mapping and `Inspect` never download connectors. Set `DownloadsEnabled`
false to prohibit downloads while permitting already active compatible installed connectors.
Inactive packs are not loaded; activate a compatible version before offline use.

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

Concurrent missing-connector reads coordinate installation and publication. Existing URLs/readers
retain their old snapshot and live connections. Already loaded assemblies are not replaced.
Changes to an already loaded connector require a new service/catalog or process; no hot replacement
is promised. Store inspection follows DubUrl's rules: installation verifies the archive digest,
while later inspection checks metadata, compatibility and required assets rather than rehashing
all installed files. Invalid binaries or missing runtime dependencies produce loading diagnostics.
An unhealthy or unloadable pack does not hide unrelated valid packs. Collisions between explicit
modules and installed modules fail deterministically instead of silently replacing registrations.

Hosts may inject an `IConnectorService` through options to control the application-service boundary,
or provide `IConnectorAvailabilityService` alongside a custom `IDatabaseConnectionService` to
`AddDatabase`. The availability result's selected runtime scheme is passed to connection opening.
Contract-supplied arbitrary download URLs are never accepted as connector sources. Catalog trust
and delivery are host responsibilities. External ODBC/OLE DB vendor drivers remain prerequisites.
Authentication, permission and connectivity failures never trigger connector reinstallation.

## Discovery and diagnostics

`connections.Inspect("postgresql")` combines catalog metadata, local store facts and the loaded
registry without opening a connection or accessing the network. Each returned `ConnectorInfo`
retains independent `Listed`, `Installed`, `Active`, and `Unhealthy` values per version, including
installed versions absent from the catalog. `Loaded` describes runtime provider availability,
not server connectivity or support for schema deployment/bulk loading. Connector IDs and aliases
resolve through catalog metadata; a multi-database connector ID is suitable for discovery but
needs a database scheme to select its runtime mapping.

Stable codes include `connector-loaded`, `connector-unknown`, `connector-not-installed`,
`connector-inactive`, `connector-unhealthy`, `connector-not-loaded`, `connector-ambiguous`,
`connector-preference-invalid`, `connector-offline`, `connector-load-failed`, and upstream
installation codes such as `pack-integrity`, `api-incompatible`, `tfm-incompatible`,
`rid-incompatible` and `connector-download-failed`. Messages identify the connector and missing
prerequisite. `DatabaseConnectionException` exposes `Code` and, where available, `EndpointId`
and `ConnectorId`. Provider/store exception details are omitted because they can contain secrets.
Do not log advanced `ConnectionUrl.Url` or `Parse()` values: those are connection material,
not diagnostics. Runtime options redact their `ToString()` representation.

## Ownership and cancellation

Every operation gets a new URL, mapper and connection. Sharing configuration never shares a live
connection. `OpenAsync` returns `DatabaseConnectionSession`; its caller owns `Connection`.
Database readers own their reader, command and connection until `Close` or `Dispose`. Opening,
connection-string assignment and query failures dispose operation-owned resources. Disposal
attempts all owned resources even if another resource's disposal throws. Cancellation flows
through installation, connection opening and asynchronous query execution. Synchronous providers
receive cancellation checks around their blocking calls; Packata cannot interrupt an uncooperative
synchronous driver. Cancellation remains `OperationCanceledException` (including DubUrl's derived
connector cancellation type) with the caller token, and upstream installation preserves consistency.

The service owns its installed-module loaders and any HTTP client it creates internally. Dispose
it after its readers. Supplied modules, registries, application connector services and HTTP clients
remain host-owned. Old loader contexts are retained while the service can still have readers using
old snapshots, then released when the service is disposed. No implicit machine-wide driver
installation, uninstall interface or background management loop is provided.

## Provisioning operation preflight

`DubUrlProvisioner.GetOperationSupport()` reports separate `Reading`, `SchemaDeployment` and
`BulkLoading` facts. Reading is generic database querying. Schema deployment uses an explicit
Packata allow-list of supported dialect types (SQL Server, DuckDB, PostgreSQL, SQLite, MySQL and
Oracle), or a `DatabaseSchemaDeploymentSupport` capability registered by the host. ANSI fallback
and arbitrary custom dialects do not imply provisioning support. Bulk loading uses the actual
upstream `ConnectionUrl.BulkCopyProvider` capability, not a dialect name or catalog listing.
Native DuckDB and SQL Server connector packs include their typed bulk adapters; a build-time
provider family without its adapter remains eligible for schema-only deployment.

Hosts can supply `IDatabaseOperationCapabilities` to the builder or provisioner when deliberately
configuring other adapters. `ExecuteAsync` checks loading and deployment support, cancellation,
and reader configuration before DDL or source reading. `DeploySchema` remains usable independently
without bulk loading. Unsupported operations throw `DatabaseOperationNotSupportedException` with
the exact operation and provider dialect; failed resolution uses `database-preflight-failed`.
Preflight establishes known support, not server permissions, deployment success or transactional rollback.

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
