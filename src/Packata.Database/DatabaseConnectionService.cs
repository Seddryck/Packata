using System.Data;
using System.Data.Common;
using DubUrl;
using DubUrl.Application;
using DubUrl.Core.ProviderPacks;
using DubUrl.Mapping;
using Packata.Core.Contracts;

namespace Packata.Database;

/// <summary>Shared configuration; every resolution and open has independent connection state.</summary>
public interface IDatabaseConnectionService
{
    ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null);
    ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>Read-only discovery and opt-in, asynchronous connector acquisition.</summary>
public interface IConnectorAvailabilityService
{
    DatabaseConnectorAvailability Inspect(string scheme);
    ValueTask<string> EnsureAvailableAsync(string scheme, CancellationToken cancellationToken = default);
}

/// <summary>The caller owns Connection and must dispose it, including after command failures.</summary>
public sealed record DatabaseConnectionSession(IDbConnection Connection, Func<string, string> RenderIdentifier);

/// <summary>Host-owned secrets and provider options, kept outside canonical contracts.</summary>
public sealed record DatabaseRuntimeOptions(string? Username = null, string? Password = null,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public override string ToString() => "Database runtime configuration (redacted)";
}

public sealed class DatabaseConnectionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public string? EndpointId { get; init; }
    public string? ConnectorId { get; init; }
}

/// <summary>Independent store facts plus actual runtime resolution; none establish server connectivity.</summary>
public sealed record DatabaseConnectorAvailability(string Scheme, bool Loaded,
    IReadOnlyList<ConnectorInfo> Connectors, IReadOnlyList<ProviderPackDiagnostic> Diagnostics,
    string Code, string Message);

/// <summary>Host configuration copied when the connection service is constructed.</summary>
public sealed class DatabaseConnectionServiceOptions
{
    public string RootPath { get; set; } = "";
    public IList<IDubUrlProviderModule> Modules { get; } = new List<IDubUrlProviderModule>();
    public bool DiscoverApplicationProviders { get; set; }
    public ProviderPackStore? ProviderStore { get; set; }
    public bool UseDefaultProviderStore { get; set; }
    public ProviderPackCatalog? Catalog { get; set; }
    public HttpClient? HttpClient { get; set; }
    public IConnectorService? ConnectorService { get; set; }
    public bool DownloadsEnabled { get; set; } = true;
    /// <summary>Map a scheme to a curated canonical connector ID when several technologies match.</summary>
    public IDictionary<string, string> ConnectorPreferences { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Func<DataEndpoint, DatabaseRuntimeOptions?>? RuntimeConfiguration { get; set; }
}

/// <summary>
/// Uses fixed DubUrl registry snapshots. Explicit modules bypass discovery. Only an explicitly
/// configured store participates; downloads occur only when opening a database with acquisition configured.
/// The host owns supplied registries, modules, connector services and HTTP clients. Dispose this service
/// after its readers to release its installed-module loaders and any internally created HTTP client.
/// </summary>
public sealed class DubUrlConnectionService : IDatabaseConnectionService, IConnectorAvailabilityService, IDisposable
{
    private sealed record Snapshot(ISchemeRegistry Registry, IReadOnlyList<ProviderPackDiagnostic> Diagnostics,
        IReadOnlySet<string> UnloadableConnectors, string StoreSignature);
    private static readonly Lazy<DubUrlConnectionService> SharedDefault = new(() => new());
    private readonly IDubUrlProviderModule[] _modules = [];
    private readonly string _rootPath = "";
    private readonly bool _discover;
    private readonly bool _downloads;
    private readonly ProviderPackStore? _store;
    private readonly ProviderPackCatalog _catalog = new([]);
    private readonly IConnectorService? _connectors;
    private readonly HttpClient? _ownedHttp;
    private readonly IReadOnlyDictionary<string, string> _preferences = new Dictionary<string, string>();
    private readonly Func<DataEndpoint, DatabaseRuntimeOptions?>? _runtime;
    private readonly SemaphoreSlim _publication = new(1, 1);
    private readonly List<InstalledProviderModuleLoader> _loaders = [];
    private Snapshot _snapshot;
    private volatile bool _disposed;

    public static DubUrlConnectionService Default => SharedDefault.Value;
    public string? ProviderStorePath => _store?.RootPath;

    /// <summary>Compatibility convenience: discover application dependencies once, with no provider store.</summary>
    public DubUrlConnectionService(string rootPath = "", Func<DataEndpoint, DatabaseRuntimeOptions?>? runtimeConfiguration = null)
        : this(new DatabaseConnectionServiceOptions
        { RootPath = rootPath, DiscoverApplicationProviders = true, RuntimeConfiguration = runtimeConfiguration }) { }

    /// <summary>Retains a fixed, host-owned registry; performs no discovery, download or global registration.</summary>
    public DubUrlConnectionService(ISchemeRegistry registry, Func<DataEndpoint, DatabaseRuntimeOptions?>? runtimeConfiguration = null)
    {
        _snapshot = new(registry ?? throw new ArgumentNullException(nameof(registry)), [], new HashSet<string>(), "");
        _runtime = runtimeConfiguration;
    }

    public DubUrlConnectionService(DatabaseConnectionServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rootPath = options.RootPath;
        _modules = options.Modules.ToArray();
        _discover = options.DiscoverApplicationProviders;
        _downloads = options.DownloadsEnabled;
        _store = ResolveStore(options);
        _catalog = options.Catalog ?? new([]);
        _preferences = new Dictionary<string, string>(options.ConnectorPreferences, StringComparer.OrdinalIgnoreCase);
        _runtime = options.RuntimeConfiguration;
        (_connectors, _ownedHttp) = CreateConnectorService(options, _store, _catalog);
        try { _snapshot = BuildSnapshot(); }
        catch { _ownedHttp?.Dispose(); throw; }
    }

    private static ProviderPackStore? ResolveStore(DatabaseConnectionServiceOptions options)
    {
        if (options.ProviderStore is not null && options.UseDefaultProviderStore)
            throw new ArgumentException("Choose an explicit provider store or opt into the default store, not both.", nameof(options));
        return options.ProviderStore ?? (options.UseDefaultProviderStore ? new ProviderPackStore() : null);
    }

    private static (IConnectorService? Service, HttpClient? OwnedHttp) CreateConnectorService(
        DatabaseConnectionServiceOptions options, ProviderPackStore? store, ProviderPackCatalog catalog)
    {
        if (options.ConnectorService is not null && store is null)
            throw new ArgumentException("Connector acquisition requires the store that will load the installed modules.", nameof(options));
        if (store is null) return (null, null);
        if (options.ConnectorService is not null) return (options.ConnectorService, null);
        var ownedHttp = options.HttpClient is null ? new HttpClient() : null;
        return (new ConnectorService(store, catalog, options.HttpClient ?? ownedHttp!), ownedHttp);
    }

    public ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ConnectionUrl(DatabaseEndpointResolver.BuildConnectionUrl(location, options), Volatile.Read(ref _snapshot).Registry);
    }

    public DatabaseConnectorAvailability Inspect(string scheme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateScheme(scheme);
        var snapshot = Volatile.Read(ref _snapshot);
        var infos = ReadConnectorFacts(snapshot);
        var candidates = Candidates(scheme);
        var relevant = infos.Where(info => candidates.Any(entry => Same(entry.Provider, info.Id))
            || info.Schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
            || Same(info.Id, scheme) || info.Aliases.Contains(scheme, StringComparer.OrdinalIgnoreCase)).ToArray();
        var loaded = snapshot.Registry.CanHandle(scheme)
            || (_catalog.TryGet(scheme, out var named) && named!.Schemes.Any(snapshot.Registry.CanHandle));
        var code = loaded ? "connector-loaded"
            : _preferences.ContainsKey(scheme) && !TryPreference(scheme, candidates, out _) ? "connector-preference-invalid"
            : candidates.Length > 1 && !TryPreference(scheme, candidates, out _) && !_catalog.TryGet(scheme, out _) ? "connector-ambiguous"
            : relevant.Any(info => info.Unhealthy) ? "connector-unhealthy"
            : relevant.Any(info => info.Active) ? "connector-not-loaded"
            : relevant.Any(info => info.Installed) ? "connector-inactive"
            : candidates.Length > 0 ? "connector-not-installed" : "connector-unknown";
        return new(scheme, loaded, relevant, snapshot.Diagnostics, code, Message(code));
    }

    public async ValueTask<string> EnsureAvailableAsync(string scheme, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateScheme(scheme);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _snapshot).Registry.CanHandle(scheme)) return scheme;
        await _publication.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await EnsureFromStoreAsync(scheme, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (DatabaseConnectionException) { throw; }
        catch (ProviderRegistrationException) { throw; }
        catch (ProviderPackException error)
        { throw new DatabaseConnectionException(error.Code, "Connector store verification failed. Check compatibility and installed assets."); }
        catch
        { throw new DatabaseConnectionException("connector-store-failed", "The configured connector store could not be loaded. Check store access and runtime dependencies."); }
        finally { _publication.Release(); }
    }

    // Called only while holding the publication gate; readers retain earlier immutable snapshots.
    private async ValueTask<string> EnsureFromStoreAsync(string scheme, CancellationToken cancellationToken)
    {
        if (_snapshot.Registry.CanHandle(scheme)) return scheme;
        if (_store is not null && Signature(_store.Inspect()) != _snapshot.StoreSignature) PublishSnapshot();
        if (_snapshot.Registry.CanHandle(scheme)) return scheme;
        CheckUnlistedConnector(scheme);
        var entry = SelectConnector(scheme);
        var runtimeScheme = RuntimeScheme(scheme, entry);
        if (_snapshot.Registry.CanHandle(runtimeScheme)) return runtimeScheme;
        await InstallConnectorAsync(entry, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PublishSnapshot();
        if (!_snapshot.Registry.CanHandle(runtimeScheme))
            throw Failure("connector-load-failed", entry.Provider, "The installed connector could not load the requested scheme. Check its compatibility, dependencies and external driver prerequisites.");
        return runtimeScheme;
    }

    private void CheckUnlistedConnector(string scheme)
    {
        if (Candidates(scheme).Length != 0) return;
        var local = ReadConnectorFacts(_snapshot).FirstOrDefault(info => info.Schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
            || Same(info.Id, scheme) || info.Aliases.Contains(scheme, StringComparer.OrdinalIgnoreCase));
        if (local is null) return;
        var code = local.Unhealthy ? "connector-unhealthy" : local.Active ? "connector-not-loaded" : "connector-inactive";
        throw Failure(code, local.Id, Message(code));
    }

    private async Task InstallConnectorAsync(ConnectorDefinition entry, CancellationToken cancellationToken)
    {
        if (!_downloads)
            throw Failure("connector-offline", entry.Provider, "Downloads are disabled. Activate a compatible installed connector or provide it at build time.");
        if (_store is null || _connectors is null)
            throw Failure("connector-store-required", entry.Provider, "Configure a provider store and the curated acquisition service.");
        // An active module which cannot load needs repair, not an automatic reinstallation loop.
        if (ReadConnectorFacts(_snapshot).Any(info => Same(info.Id, entry.Provider) && info.Active))
            throw Failure("connector-load-failed", entry.Provider, "The active connector could not load. Check managed/native dependencies and external ODBC/OLE DB drivers.");
        try { await _connectors.InstallAsync(entry.Provider, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (ConnectorOperationException error)
        { throw Failure(error.Code, entry.Provider, "Connector acquisition failed. Check the curated archive, compatibility, network and store access."); }
        catch (ProviderPackException error)
        { throw Failure(error.Code, entry.Provider, "Connector verification or installation failed."); }
        catch
        { throw Failure("connector-install-failed", entry.Provider, "Connector acquisition failed. Check network and store access."); }
    }

    public async ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Location is not ConnectionLocation location)
            throw new ArgumentException("A database endpoint requires a connection location.", nameof(endpoint));
        IDbConnection? connection = null;
        try
        {
            var url = DatabaseEndpointResolver.BuildConnectionUrl(location, _runtime?.Invoke(endpoint));
            var scheme = new Uri(url).Scheme;
            var resolvedScheme = await EnsureAvailableAsync(scheme, cancellationToken).ConfigureAwait(false);
            if (!Same(scheme, resolvedScheme)) url = resolvedScheme + url[url.IndexOf("://", StringComparison.Ordinal)..];
            var connectionUrl = new ConnectionUrl(url, Volatile.Read(ref _snapshot).Registry);
            var dialect = connectionUrl.Dialect;
            connection = connectionUrl.Connect();
            if (connection is DbConnection asyncConnection)
                await asyncConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            else { cancellationToken.ThrowIfCancellationRequested(); connection.Open(); }
            cancellationToken.ThrowIfCancellationRequested();
            return new(connection, value => DatabaseIdentifierRenderer.Render(dialect, value));
        }
        catch (OperationCanceledException) { connection?.Dispose(); throw; }
        catch (ProviderRegistrationException) { connection?.Dispose(); throw; }
        catch (DatabaseConnectionException error)
        {
            connection?.Dispose();
            throw new DatabaseConnectionException(error.Code, error.Message)
                { EndpointId = endpoint.Id, ConnectorId = error.ConnectorId };
        }
        catch
        {
            connection?.Dispose();
            throw new DatabaseConnectionException("database-open-failed",
                "Database opening failed. Check the configured provider, external driver, runtime credentials and server connectivity. Connector installation is not retried for connection failures.")
                { EndpointId = endpoint.Id };
        }
    }

    private Snapshot BuildSnapshot()
    {
        var builder = new ProviderCatalogBuilder().WithRootPath(_rootPath);
        foreach (var module in _modules) builder.AddModule(module);
        var diagnostics = new List<ProviderPackDiagnostic>();
        var unloadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signature = "";
        InstalledProviderModuleLoader? loader = null;
        try
        {
            if (_store is not null)
            {
                var installed = _store.Inspect();
                signature = Signature(installed);
                foreach (var pack in installed) diagnostics.AddRange(pack.Diagnostics);
                loader = new InstalledProviderModuleLoader();
                var loadingDiagnostics = new List<ProviderPackDiagnostic>();
                foreach (var module in loader.LoadActive(_store, loadingDiagnostics))
                {
                    var registrations = new List<ProviderRegistration>();
                    try
                    {
                        var probe = new ProviderCatalogBuilder();
                        module.Register(new RegistrationProbe(probe, registrations));
                        _ = probe.Build();
                    }
                    catch (ProviderRegistrationException) { throw; }
                    catch
                    {
                        foreach (var pack in installed.Where(pack => registrations.Any(registration =>
                            Same(registration.ProviderInvariantName, pack.Manifest.Driver)
                            && registration.Aliases.Any(alias => pack.Manifest.Schemes.Contains(alias, StringComparer.OrdinalIgnoreCase)))))
                            unloadable.Add(pack.Manifest.Provider);
                        loadingDiagnostics.Add(new("module-load-failed", "A connector provider factory could not load."));
                        continue;
                    }
                    builder.AddModule(module); // Explicit/installed collisions fail with both registration names.
                }
                foreach (var pack in installed)
                    if (loadingDiagnostics.Any(d => d.Message.StartsWith($"Provider '{pack.Manifest.Provider}' ", StringComparison.Ordinal)))
                        unloadable.Add(pack.Manifest.Provider);
                diagnostics.AddRange(loadingDiagnostics);
            }
            if (_discover) builder.AddApplicationProviders();
            var registry = builder.Build();
            if (loader is not null) { _loaders.Add(loader); loader = null; }
            return new(registry, diagnostics.Select(d => new ProviderPackDiagnostic(d.Code,
                "Connector diagnostic: " + d.Code + ". Check pack compatibility, installed assets and driver prerequisites.")).ToArray(), unloadable, signature);
        }
        finally { loader?.Dispose(); }
    }

    private void PublishSnapshot() => Volatile.Write(ref _snapshot, BuildSnapshot());

    private sealed class RegistrationProbe(IProviderCatalogBuilder inner, List<ProviderRegistration> registrations) : IProviderCatalogBuilder
    {
        public IProviderCatalogBuilder AddProvider(ProviderRegistration registration)
        { registrations.Add(registration); inner.AddProvider(registration); return this; }
        public IProviderCatalogBuilder AddOdbcLocatorOption<TOption>() where TOption : struct, Enum
        { inner.AddOdbcLocatorOption<TOption>(); return this; }
    }

    private IReadOnlyList<ConnectorInfo> ReadConnectorFacts(Snapshot snapshot)
    {
        IReadOnlyList<ConnectorInfo> infos;
        try
        {
            infos = _connectors?.List() ?? _catalog.Entries.Select(entry => new ConnectorInfo(entry.Provider,
                entry.Version, "catalog", "listed", true, false, false, false, entry.Aliases, entry.Schemes, [])).ToArray();
        }
        catch { throw new DatabaseConnectionException("connector-list-failed", "The configured connector store could not be inspected."); }
        return infos.Select(info => info with
        {
            Unhealthy = info.Unhealthy || snapshot.UnloadableConnectors.Contains(info.Id),
            State = snapshot.UnloadableConnectors.Contains(info.Id) ? "unhealthy" : info.State,
            Diagnostics = info.Diagnostics.Select(d => new ProviderPackDiagnostic(d.Code, "Connector diagnostic: " + d.Code))
                .Concat(snapshot.UnloadableConnectors.Contains(info.Id)
                    ? new[] { new ProviderPackDiagnostic("module-load-failed", "The connector module or provider factory could not load. Check its runtime dependencies.") } : []).ToArray()
        }).ToArray();
    }

    private ConnectorDefinition[] Candidates(string scheme)
    {
        _catalog.TryGet(scheme, out var exact);
        if (exact is not null && Same(exact.Provider, scheme)) return [exact];
        return _catalog.Entries.Where(entry => ReferenceEquals(entry, exact)
            || entry.Schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private bool TryPreference(string scheme, ConnectorDefinition[] candidates, out ConnectorDefinition? selected)
    {
        selected = _preferences.TryGetValue(scheme, out var preferred)
            ? candidates.FirstOrDefault(entry => Same(entry.Provider, preferred) || entry.Aliases.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            : null;
        return selected is not null;
    }

    private ConnectorDefinition SelectConnector(string scheme)
    {
        var candidates = Candidates(scheme);
        if (TryPreference(scheme, candidates, out var selected)) return selected!;
        if (_preferences.ContainsKey(scheme))
            throw new DatabaseConnectionException("connector-preference-invalid", "The configured connector preference does not match this scheme's curated connectors.");
        if (_catalog.TryGet(scheme, out var alias)) return alias!;
        if (candidates.Length == 1) return candidates[0];
        throw new DatabaseConnectionException(candidates.Length == 0 ? "connector-unknown" : "connector-ambiguous",
            candidates.Length == 0 ? Message("connector-unknown") : Message("connector-ambiguous"));
    }

    private static string RuntimeScheme(string requested, ConnectorDefinition entry)
    {
        if (entry.Schemes.Contains(requested, StringComparer.OrdinalIgnoreCase)) return requested;
        if (entry.Databases.Count == 1) return entry.Schemes[0];
        throw Failure("connector-scheme-ambiguous", entry.Provider, "Specify a database scheme from this connector's metadata, rather than its connector ID.");
    }

    private static bool Same(string first, string second) => StringComparer.OrdinalIgnoreCase.Equals(first, second);
    private static string Signature(IReadOnlyList<InstalledProviderPack> packs) => string.Join('|', packs.Select(pack =>
        $"{pack.Manifest.Provider}/{pack.Manifest.Version}/{pack.Status}/{string.Join(',', pack.Diagnostics.Select(d => d.Code))}"));
    private static void ValidateScheme(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme) || !Uri.CheckSchemeName(scheme))
            throw new DatabaseConnectionException("connector-scheme-invalid", "Supply a scheme or curated connector ID, not a connection URL.");
    }
    private static DatabaseConnectionException Failure(string code, string connector, string message) =>
        new(code, $"Connector '{connector}': {message}") { ConnectorId = connector };
    private static string Message(string code) => code switch
    {
        "connector-loaded" => "A provider is loaded. This does not establish connectivity or provisioning support.",
        "connector-ambiguous" => "Several curated connection technologies match. Configure a canonical connector preference.",
        "connector-unknown" => "No loaded provider or curated connector matches. Configure an approved connector catalog or explicit module.",
        "connector-preference-invalid" => "The configured connector preference does not match this scheme's curated connectors.",
        "connector-unhealthy" => "An installed connector is unhealthy. Check compatibility, installed assets and driver prerequisites.",
        "connector-inactive" => "The connector is installed but inactive. Activate a compatible version before offline use.",
        "connector-not-loaded" => "An active connector is not loaded. Check module and external driver prerequisites.",
        _ => "The curated connector is not installed. Configure acquisition or install it in the selected store."
    };

    public void Dispose()
    {
        _publication.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var loader in _loaders) loader.Dispose();
            _loaders.Clear();
            _ownedHttp?.Dispose();
        }
        finally { _publication.Release(); }
    }
}
