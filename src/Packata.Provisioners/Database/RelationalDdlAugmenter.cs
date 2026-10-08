using Packata.Core.Contracts;
using Packata.Core.Provisioning;

namespace Packata.Provisioners.Database;

internal sealed class RelationalDdlAugmenter(string connectionUrl)
{
    private readonly string _scheme = connectionUrl.Split(':', 2)[0].Split('+').Last().ToLowerInvariant();

    public string Render(DataContract contract, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, new[]
        {
            RenderForeignKeys(contract, options, diagnostics),
            RenderPatternChecks(contract, options, diagnostics)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string RenderForeignKeys(DataContract contract, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (!options.Constraints.HasFlag(ContractConstraintOptions.ForeignKeys)) return string.Empty;
        var statements = new List<string>();
        foreach (var asset in contract.Assets)
        {
            if (asset.Schema is null) continue;
            for (var index = 0; index < asset.Schema.Relationships.Count; index++)
            {
                var relationship = asset.Schema.Relationships[index];
                if (!relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)) continue;
                var target = contract.Assets.FirstOrDefault(value => value.Id == relationship.TargetAsset
                    || value.Name == relationship.TargetAsset);
                if (target?.Schema is null || relationship.Fields.Count == 0
                    || relationship.Fields.Count != relationship.TargetFields.Count
                    || !TryColumns(asset.Schema, relationship.Fields, out var sourceColumns)
                    || !TryColumns(target.Schema, relationship.TargetFields, out var targetColumns))
                {
                    diagnostics.Add(new("PROV003", asset.Id,
                        $"Relationship to '{relationship.TargetAsset}' does not resolve to compatible fields."));
                    continue;
                }

                var name = relationship.Name ?? $"FK_{asset.Id}_{target.Id}_{index + 1}";
                statements.Add($"ALTER TABLE {Quote(asset.PhysicalName ?? asset.Name)} ADD CONSTRAINT {Quote(name)} " +
                    $"FOREIGN KEY ({Join(sourceColumns)}) REFERENCES {Quote(target.PhysicalName ?? target.Name)} ({Join(targetColumns)});");
            }
        }
        return string.Join(Environment.NewLine, statements);
    }

    private string RenderPatternChecks(DataContract contract, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (!options.Constraints.HasFlag(ContractConstraintOptions.Checks)) return string.Empty;
        var statements = new List<string>();
        foreach (var asset in contract.Assets.Where(value => value.Schema is not null))
        foreach (var field in asset.Schema!.Fields)
        foreach (var constraint in field.Constraints.Where(value =>
                     value.Kind.Equals("pattern", StringComparison.OrdinalIgnoreCase)))
        {
            var op = _scheme switch
            {
                "postgres" or "postgresql" or "pg" or "pgsql" or "mysql" or "my" or "maria" or "mariadb"
                    => _scheme.StartsWith("p") ? "~" : "REGEXP",
                _ => null
            };
            if (op is null || constraint.Value is not string pattern)
            {
                diagnostics.Add(new("PROV004", asset.Id,
                    $"Constraint 'pattern' on field '{field.Name}' is not supported by target '{_scheme}'."));
                continue;
            }
            var table = Quote(asset.PhysicalName ?? asset.Name);
            var column = Quote(field.PhysicalName ?? field.Name);
            var name = Quote($"CK_{asset.Id}_{field.Name}_pattern");
            statements.Add($"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({column} {op} {Literal(pattern)});" );
        }
        return string.Join(Environment.NewLine, statements);
    }

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";

    private static bool TryColumns(DataSchema schema, IReadOnlyList<string> names, out string[] columns)
    {
        columns = names.Select(name => schema.Fields.FirstOrDefault(field => field.Name == name))
            .Where(field => field is not null).Select(field => field!.PhysicalName ?? field.Name).ToArray();
        return columns.Length == names.Count;
    }

    private string Join(IEnumerable<string> values) => string.Join(", ", values.Select(Quote));

    internal string Quote(string identifier)
    {
        if (_scheme is "mysql" or "my" or "maria" or "mariadb")
            return $"`{identifier.Replace("`", "``")}`";
        if (_scheme is "mssql" or "ms" or "sqlserver" or "mssqlserver")
        {
            var escaped = identifier.Replace("]", "]]");
            return $"[{escaped}]";
        }
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
