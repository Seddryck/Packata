#nullable enable
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Sql;

internal sealed record SqlCompilationBinding(DataAsset Asset, DataEndpoint Endpoint, SqlDialect Dialect, string? AssetPath)
{
    internal static SqlCompilationBinding? Resolve(DataQualityRule rule, RuleCompilationContext context,
        SqlRuleOptions options, List<CompilationFinding> findings)
    {
        void Missing(string code, string message) => findings.Add(new(rule.ExecutionId, CompilationFindingKind.MissingBinding, code, message));
        var assets = context.Contract?.Assets.Where(a => a.Id == rule.Target?.AssetId).ToArray() ?? [];
        if (assets.Length != 1) { Missing("SQL_ASSET", "Select one canonical asset for the SQL target."); return null; }
        var asset = assets[0];
        var endpointId = ResolveEndpointId(asset.EndpointBindings, context.EndpointId, Missing);
        if (endpointId is null) return null;
        var endpoint = ResolveEndpoint(context.Contract!, endpointId, Missing);
        if (endpoint is null) return null;
        var location = (ConnectionLocation)endpoint.Location;
        var dialect = options.AuthoredDialect ?? ResolveDialect(location.Scheme)
            ?? (Uri.TryCreate(location.ConnectionUrl, UriKind.Absolute, out var url) ? ResolveDialect(url.Scheme) : null);
        if (dialect is null || !Enum.IsDefined(dialect.Value))
        {
            Missing("SQL_DIALECT", "Declare the authored SQL dialect for this endpoint scheme."); return null;
        }
        return new(asset, endpoint, dialect.Value, asset.EndpointBindings.SingleOrDefault(b => b.EndpointId == endpointId)?.AssetPath);
    }

    private static string? ResolveEndpointId(IReadOnlyList<EndpointBinding> bindings, string? selected, Action<string, string> missing)
    {
        var endpointId = selected ?? (bindings.Count == 1 ? bindings[0].EndpointId : null);
        if (endpointId is null) missing("SQL_ENDPOINT", "Select one endpoint explicitly when bindings are absent or ambiguous.");
        var matches = bindings.Count(b => b.EndpointId == endpointId);
        if ((endpointId is not null && bindings.Count > 0 && matches == 0) || matches > 1)
        {
            missing("SQL_ENDPOINT_BINDING", "The selected endpoint must have one unambiguous binding to the target asset."); return null;
        }
        return endpointId;
    }

    private static DataEndpoint? ResolveEndpoint(DataContract contract, string id, Action<string, string> missing)
    {
        var matches = contract.Endpoints.Where(e => e.Id == id).ToArray();
        if (matches.Length == 1 && matches[0].Location is ConnectionLocation) return matches[0];
        missing("SQL_DATABASE", "The selected endpoint must expose one unambiguous database connection location.");
        return null;
    }

    internal IReadOnlyList<string> ObjectPath()
    {
        var location = (ConnectionLocation)Endpoint.Location;
        var name = AssetPath ?? Asset.PhysicalName ?? Asset.Name;
        return new[] { location.Catalog, location.Namespace, name }.OfType<string>().Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
    }

    internal IReadOnlyList<string> PhysicalFieldPath(IReadOnlyList<string> path)
    {
        var fields = Asset.Schema?.Fields ?? [];
        var result = new List<string>();
        foreach (var segment in path)
        {
            var matches = fields.Where(f => f.Name == segment).ToArray();
            if (matches.Length != 1) throw new ArgumentException("The logical field path is missing or ambiguous.");
            var field = matches[0];
            result.Add(field.PhysicalName ?? field.Name);
            fields = field.Children;
        }
        return result.AsReadOnly();
    }

    private static SqlDialect? ResolveDialect(string scheme) => scheme.ToLowerInvariant() switch
    {
        "sqlite" or "sqlite3" => SqlDialect.Sqlite,
        "postgres" or "postgresql" or "pgsql" => SqlDialect.PostgreSql,
        "mssql" or "sqlserver" => SqlDialect.SqlServer,
        "mysql" or "mariadb" => SqlDialect.MySql,
        "duckdb" => SqlDialect.DuckDb,
        _ => null
    };
}
