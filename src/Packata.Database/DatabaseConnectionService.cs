using System.Data;
using System.Data.Common;
using System.Collections.Concurrent;
using DubUrl;
using DubUrl.Mapping;
using DubUrl.Parsing;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Parametrizing;
using DubUrl.Registering;
using Packata.Core.Contracts;

namespace Packata.Database;

/// <summary>Shared configuration; every resolution and open has independent mutable connection state.</summary>
public interface IDatabaseConnectionService
{
    ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null);
    ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>The caller owns and must dispose Connection, including after command failures.</summary>
public sealed record DatabaseConnectionSession(IDbConnection Connection, Func<string, string> RenderIdentifier);

/// <summary>Host-owned secrets and provider options. Do not store these in canonical contracts or logs.</summary>
public sealed record DatabaseRuntimeOptions(string? Username = null, string? Password = null,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public override string ToString() => "Database runtime configuration (redacted)";
}

/// <summary>Stable, credential-free database diagnostic. Underlying exceptions are deliberately omitted.</summary>
public sealed class DatabaseConnectionException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// DubUrl 0.29/0.30 adapter. Registry discovery and legacy process-wide provider registration occur
/// during initialization only. Custom registries remain host-owned; custom mapper factories must
/// return a new mapper per call. This service holds no open connections and requires no disposal.
/// </summary>
public sealed class DubUrlConnectionService : IDatabaseConnectionService
{
    private static readonly Lazy<bool> Registration = new(() =>
    {
        new ProviderFactoriesRegistrator(typeof(DubUrlConnectionService).Assembly).Register();
        return true;
    });
    private static readonly Lazy<DubUrlConnectionService> SharedDefault = new(() => new());
    private readonly ISchemeRegistry _registry;
    private readonly string _rootPath;
    private readonly Func<string, IMapper>? _mapperFactory;
    private readonly Func<DataEndpoint, DatabaseRuntimeOptions?>? _runtime;
    private readonly ConcurrentDictionary<string, Func<IMapper>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public static DubUrlConnectionService Default => SharedDefault.Value;

    public DubUrlConnectionService(string rootPath = "",
        Func<DataEndpoint, DatabaseRuntimeOptions?>? runtimeConfiguration = null)
    {
        _ = Registration.Value;
        _rootPath = rootPath;
        _runtime = runtimeConfiguration;
        _registry = new SchemeRegistryBuilder().WithRootPath(rootPath)
            .WithAssemblies(typeof(SchemeRegistryBuilder).Assembly).WithAutoDiscoveredMappings().Build();
    }

    /// <summary>
    /// Uses an existing registry without discovery or global registration. For nonstandard mapper
    /// constructors supply mapperFactory. Standard DubUrl mappers are recreated with fresh builders.
    /// </summary>
    public DubUrlConnectionService(ISchemeRegistry registry, string rootPath = "",
        Func<string, IMapper>? mapperFactory = null,
        Func<DataEndpoint, DatabaseRuntimeOptions?>? runtimeConfiguration = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _rootPath = rootPath;
        _mapperFactory = mapperFactory;
        _runtime = runtimeConfiguration;
    }

    public ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null)
    {
        var url = DatabaseEndpointResolver.BuildConnectionUrl(location, options);
        return new OwnedConnectionUrl(url, new OperationRegistry(this));
    }

    public async ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Location is not ConnectionLocation location)
            throw new ArgumentException("A database endpoint requires a connection location.", nameof(endpoint));
        IDbConnection? connection = null;
        try
        {
            var url = Resolve(location, _runtime?.Invoke(endpoint));
            var dialect = url.Dialect;
            connection = url.Connect();
            if (connection is DbConnection asyncConnection)
                await asyncConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Open();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(connection, value => DatabaseIdentifierRenderer.Render(dialect, value));
        }
        catch (OperationCanceledException) { connection?.Dispose(); throw; }
        catch (DatabaseConnectionException) { connection?.Dispose(); throw; }
        catch
        {
            connection?.Dispose();
            throw new DatabaseConnectionException("database-open-failed",
                "Database opening failed. Check the configured provider, external driver, runtime credentials and server connectivity.");
        }
    }

    private IMapper CreateMapper(string scheme) =>
        _mapperFactory is not null ? _mapperFactory(scheme) : _factories.GetOrAdd(scheme, CreateFactory)();

    private Func<IMapper> CreateFactory(string scheme)
    {
        // Older DubUrl registries retain mutable mapper instances. Never rewrite those instances:
        // this would race and retain credentials/options from a preceding operation.
        var prototype = _registry.GetMapper(scheme);
        var provider = _registry.GetProviderFactory(scheme.Split('+'));
        var dialect = prototype.GetDialect();
        var parameterType = prototype.GetParametrizer().GetType();
        Type[] signature = [typeof(DbConnectionStringBuilder), typeof(IDialect), typeof(IParametrizer)];
        var fileBased = prototype is IFileBasedMapper;
        if (fileBased) signature = [.. signature, typeof(string)];
        var constructor = prototype.GetType().GetConstructor(signature)
            ?? throw new DatabaseConnectionException("database-mapper-factory-required",
                "The configured mapper requires an explicit factory returning a fresh mapper for each operation.");
        return () =>
        {
            object[] arguments = [provider.CreateConnectionStringBuilder()!, dialect,
                Activator.CreateInstance(parameterType)!];
            if (fileBased) arguments = [.. arguments, _rootPath];
            return (IMapper)constructor.Invoke(arguments);
        };
    }

    private sealed class OperationRegistry(DubUrlConnectionService owner) : ISchemeRegistry
    {
        private IMapper? _mapper;
        public IMapper GetMapper(string alias) => _mapper ??= new DecodingMapper(owner.CreateMapper(alias));
        public IMapper GetMapper(string[] aliases) => GetMapper(string.Join('+', aliases));
        public DbProviderFactory GetProviderFactory(string[] aliases) => owner._registry.GetProviderFactory(aliases);
        public bool CanHandle(string scheme) => owner._registry.CanHandle(scheme);
    }

    // DubUrl 0.29/0.30 leaves user info and path segments escaped. Decode only at the mapper boundary.
    private sealed class DecodingMapper(IMapper inner) : IMapper
    {
        public IReadOnlyDictionary<string, object> Rewrite(UrlInfo info) => inner.Rewrite(info with
        {
            Username = Uri.UnescapeDataString(info.Username), Password = Uri.UnescapeDataString(info.Password),
            Segments = info.Segments.Select(Uri.UnescapeDataString).ToArray()
        });
        public string GetConnectionString() => inner.GetConnectionString();
        public IDialect GetDialect() => inner.GetDialect();
        public IConnectivity GetConnectivity() => inner.GetConnectivity();
        public string GetProviderName() => inner.GetProviderName();
        public IParametrizer GetParametrizer() => inner.GetParametrizer();
    }

    private sealed class OwnedConnectionUrl(string url, ISchemeRegistry registry) : ConnectionUrl(url, registry)
    {
        public override IDbConnection Connect()
        {
            var connectionString = Parse();
            var connection = GetProviderFactory().CreateConnection()
                ?? throw new DatabaseConnectionException("database-connection-unavailable", "The provider did not create a connection.");
            try { connection.ConnectionString = connectionString; return connection; }
            catch { connection.Dispose(); throw; }
        }
        public override IDbConnection Open()
        {
            var connection = Connect();
            try { connection.Open(); return connection; }
            catch { connection.Dispose(); throw; }
        }
    }
}
