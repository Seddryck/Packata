using Packata.Core.Contracts;
using Packata.Core.Provisioning;

namespace Packata.Provisioners.Database;

internal sealed class RelationalDdlAugmenter(string connectionUrl)
{
    private readonly string _scheme = connectionUrl.Split(':', 2)[0].Split('+').Last().ToLowerInvariant();

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
