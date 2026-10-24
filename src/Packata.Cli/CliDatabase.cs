using DubUrl;
using DubUrl.Application;
using DubUrl.Core.ProviderPacks;
using Packata.Core.Contracts;
using Packata.Database;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;

namespace Packata.Cli;

/// <summary>Invocation-owned connection configuration, shared by inference and resource readers.</summary>
internal sealed class CliDatabase : IDisposable
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _overrides;
    public DubUrlConnectionService Connections { get; }

    public CliDatabase(CliConfiguration configuration, string rootPath, IReadOnlyList<DataEndpoint> endpoints,
        string[] preferences, string[] connectionEnvironment, HttpClient? http = null)
    {
        _overrides = ResolveOverrides(endpoints, connectionEnvironment);
        var options = new DatabaseConnectionServiceOptions
        {
            RootPath = rootPath, ProviderStore = configuration.Store, Catalog = configuration.Catalog(),
            DownloadsEnabled = !configuration.Offline
        };
        var selected = configuration.Preferences();
        foreach (var pair in ParsePairs(preferences, "--connector"))
        {
            if (!Uri.CheckSchemeName(pair.Key)) throw new CliException("--connector requires a valid scheme and canonical connector ID.");
            selected[pair.Key] = pair.Value;
        }
        foreach (var pair in selected) options.ConnectorPreferences[pair.Key] = pair.Value;
        _http = http ?? new HttpClient();
        options.HttpClient = _http;
        options.ConnectorService = new ReportingConnectorService(new ConnectorService(options.ProviderStore, options.Catalog, _http), options.Catalog);
        try { Connections = new DubUrlConnectionService(options); }
        catch { _http.Dispose(); throw; }
    }

    public static Dictionary<string, string> ParsePairs(IEnumerable<string> values, string option)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var parts = value.Split('=', 2);
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || !result.TryAdd(parts[0], parts[1]))
                throw new CliException($"{option} requires distinct KEY=VALUE pairs.");
        }
        return result;
    }

    internal static Dictionary<string, string> ResolveOverrides(IReadOnlyList<DataEndpoint> endpoints,
        IEnumerable<string> values, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in ParsePairs(values, "--connection-env"))
        {
            var matches = endpoints.Where(endpoint => endpoint.Id == pair.Key && endpoint.Location is ConnectionLocation).ToArray();
            if (matches.Length != 1) throw new CliException("--connection-env must name a unique database endpoint in the contract.");
            var url = environment(pair.Value);
            if (string.IsNullOrWhiteSpace(url)) throw new CliException("A --connection-env variable is missing or empty.");
            // Validate without ever echoing the supplied URL or variable contents.
            try { DatabaseEndpointResolver.BuildConnectionUrl(new("runtime", ConnectionUrl: url)); }
            catch { throw new CliException("A --connection-env variable does not contain a valid database connection URL."); }
            result.Add(pair.Key, url);
        }
        return result;
    }

    public bool HasOverride(string endpointId) => _overrides.ContainsKey(endpointId);
    public DataEndpoint RuntimeEndpoint(DataEndpoint endpoint) => _overrides.TryGetValue(endpoint.Id, out var url)
        ? endpoint with { Location = ((ConnectionLocation)endpoint.Location) with { ConnectionUrl = url } } : endpoint;

    public async Task<ConnectionUrl> ResolveAsync(DataEndpoint endpoint, CancellationToken cancellationToken)
    {
        var location = (ConnectionLocation)RuntimeEndpoint(endpoint).Location;
        var url = DatabaseEndpointResolver.BuildConnectionUrl(location);
        var scheme = new Uri(url).Scheme;
        var runtimeScheme = await Connections.EnsureAvailableAsync(scheme, cancellationToken).ConfigureAwait(false);
        if (!scheme.Equals(runtimeScheme, StringComparison.OrdinalIgnoreCase))
            location = location with { ConnectionUrl = runtimeScheme + url[url.IndexOf("://", StringComparison.Ordinal)..] };
        return Connections.Resolve(location);
    }

    public ResourceReaderFactory CreateReaders() => ResourceReaderFactory.Create(options =>
        options.AddDatabase(new RuntimeConnections(this)));

    private sealed class RuntimeConnections(CliDatabase owner) : IDatabaseConnectionService
    {
        public ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null) => owner.Connections.Resolve(location, options);
        public ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default)
            => owner.Connections.OpenAsync(owner.RuntimeEndpoint(endpoint), cancellationToken);
    }

    private sealed class ReportingConnectorService(IConnectorService inner, ProviderPackCatalog catalog) : IConnectorService
    {
        public IReadOnlyList<ConnectorInfo> List() => inner.List();
        public Task<ConnectorInfo> InstallAsync(string connector, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (catalog.TryGet(connector, out var definition)
                && (definition!.DownloadUri is null || string.IsNullOrWhiteSpace(definition.Sha256)))
                throw new ConnectorOperationException("connector-distribution-missing", "A distribution catalog with archive URLs and hashes is required.");
            Console.Error.WriteLine($"Acquiring connector {connector}.");
            return inner.InstallAsync(connector, cancellationToken);
        }
        public string Uninstall(string connector) => inner.Uninstall(connector);
    }

    public void Dispose() { Connections.Dispose(); _http.Dispose(); }
}
