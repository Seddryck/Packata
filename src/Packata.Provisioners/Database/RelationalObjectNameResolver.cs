using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;

namespace Packata.Provisioners.Database;

internal sealed class RelationalObjectNameResolver(IDialect dialect, DataContract contract)
{
    public DatabaseObjectName Resolve(DataAsset asset, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var table = asset.PhysicalName ?? asset.Name;
        if (asset.EndpointBindings.Count == 0) return new(table);
        if (asset.EndpointBindings.Count > 1)
        {
            diagnostics.Add(new("PROV007", asset.Id,
                "Multiple endpoint bindings cannot be used to determine one target database object name; the table was left unqualified."));
            return new(table);
        }

        var binding = asset.EndpointBindings[0];
        var endpoint = contract.Endpoints.FirstOrDefault(value => value.Id == binding.EndpointId);
        if (endpoint is null)
        {
            diagnostics.Add(new("PROV007", asset.Id,
                $"Endpoint binding '{binding.EndpointId}' cannot be resolved; the table was left unqualified."));
            return new(table);
        }
        if (endpoint.Location is not ConnectionLocation location) return new(table);

        var supported = (dialect.Capabilities ?? DialectCapabilities.Default).ObjectNames;
        var catalog = Qualifier(location.Catalog, ObjectNameParts.Catalog, "catalog", supported, asset, diagnostics);
        var database = Qualifier(location.Database, ObjectNameParts.Database, "database", supported, asset, diagnostics);
        var schema = Qualifier(location.Namespace, ObjectNameParts.Schema, "schema/namespace", supported, asset, diagnostics);
        return new(table, schema, database, catalog);
    }

    private static string? Qualifier(string? value, ObjectNameParts part, string label,
        ObjectNameParts supported, DataAsset asset, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (supported.HasFlag(part)) return value;
        diagnostics.Add(new("PROV007", asset.Id,
            $"The target dialect does not support the {label} qualifier '{value}'; that qualifier was omitted."));
        return null;
    }
}
