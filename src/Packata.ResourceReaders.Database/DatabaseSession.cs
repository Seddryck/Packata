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
        var factory = new ConnectionUrlFactory(new SchemeRegistryBuilder().WithRootPath(rootPath)
            .WithAssemblies(typeof(SchemeRegistryBuilder).Assembly).WithAutoDiscoveredMappings().Build());
        var connectionUrl = factory.Instantiate(BuildConnectionUrl(location));
        return new DatabaseSession(connectionUrl.Open(),
            value => connectionUrl.Dialect.Renderer.Render(value, "identity"));
    }

    internal static string BuildConnectionUrl(ConnectionLocation location)
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

        var host = string.IsNullOrWhiteSpace(location.Host) ? "." : location.Host.Trim();
        var port = location.Port is null ? string.Empty : $":{location.Port}";
        var database = string.IsNullOrWhiteSpace(location.Database)
            ? string.Empty
            : $"/{location.Database.Trim().TrimStart('/')}";
        return $"{location.Scheme.Trim()}://{host}{port}{database}";
    }
}
