using Packata.Core.Contracts;

namespace Packata.Provisioners.Database;

internal sealed class RelationalObjectNameResolver(string connectionUrl, DataContract contract)
{
    private readonly string _scheme = connectionUrl.Split(':', 2)[0].Split('+').Last().ToLowerInvariant();

    public string Resolve(DataAsset asset)
    {
        var table = asset.PhysicalName ?? asset.Name;
        var binding = asset.EndpointBindings.FirstOrDefault();
        var endpoint = binding is null ? null : contract.Endpoints.FirstOrDefault(value => value.Id == binding.EndpointId);
        if (endpoint?.Location is not ConnectionLocation location) return table;
        var qualifiers = _scheme switch
        {
            "postgres" or "postgresql" or "pg" or "pgsql" => Values(location.Namespace),
            "mysql" or "my" or "maria" or "mariadb" => Values(location.Database),
            "mssql" or "ms" or "sqlserver" or "mssqlserver"
                => Values(location.Catalog ?? location.Database, location.Namespace),
            _ => Values(location.Catalog, location.Database, location.Namespace)
        };
        return string.Join(".", qualifiers.Append(table));
    }

    private static IEnumerable<string> Values(params string?[] values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct();
}
