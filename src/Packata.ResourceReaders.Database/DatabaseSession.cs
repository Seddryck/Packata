using System.Data;
using DubUrl;
using DubUrl.Mapping;
using DubUrl.Registering;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders.Database;

internal interface IDatabaseSessionFactory
{
    DatabaseSession Open(ConnectionLocation location);
}

internal sealed record DatabaseSession(IDbConnection Connection, Func<string, string> RenderIdentifier);

internal sealed class DubUrlDatabaseSessionFactory(string rootPath) : IDatabaseSessionFactory
{
    public DatabaseSession Open(ConnectionLocation location)
    {
        new ProviderFactoriesRegistrator().Register();
        var factory = new ConnectionUrlFactory(DefaultRegistry.Get());
        var connectionUrl = factory.Instantiate(BuildConnectionUrl(location, rootPath));
        return new DatabaseSession(connectionUrl.Open(),
            value => connectionUrl.Dialect.Renderer.Render(value, "identity"));
    }

    internal static string BuildConnectionUrl(ConnectionLocation location)
        => BuildConnectionUrl(location, string.Empty);

    private static string BuildConnectionUrl(ConnectionLocation location, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!string.IsNullOrWhiteSpace(location.ConnectionUrl)) return location.ConnectionUrl;
        if (string.IsNullOrWhiteSpace(location.Scheme) || !Uri.CheckSchemeName(location.Scheme))
            throw new ArgumentException("A valid database connection scheme is required.", nameof(location));
        if (string.IsNullOrWhiteSpace(location.Host) && string.IsNullOrWhiteSpace(location.Database))
            throw new ArgumentException(
                "A database connection requires a host or database when ConnectionUrl is not provided.",
                nameof(location));
        if (location.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(location), "The database connection port is invalid.");

        if (!string.IsNullOrWhiteSpace(rootPath)
            && string.IsNullOrWhiteSpace(location.Host)
            && !string.IsNullOrWhiteSpace(location.Database)
            && !Path.IsPathRooted(location.Database)
            && IsFileDatabaseScheme(location.Scheme))
        {
            var absolute = Path.GetFullPath(Path.Combine(rootPath, location.Database))
                .Replace(Path.DirectorySeparatorChar, '/');
            var path = string.Join('/', absolute.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
            return $"{location.Scheme.Trim()}:///{path}";
        }

        var host = string.IsNullOrWhiteSpace(location.Host) ? "." : location.Host.Trim();
        var port = location.Port is null ? string.Empty : $":{location.Port}";
        var database = string.IsNullOrWhiteSpace(location.Database)
            ? string.Empty
            : $"/{location.Database.Trim().TrimStart('/')}";
        return $"{location.Scheme.Trim()}://{host}{port}{database}";
    }

    private static bool IsFileDatabaseScheme(string scheme) =>
        scheme.Split('+', 2)[0].Trim().ToLowerInvariant() is "duckdb" or "sqlite";
}
