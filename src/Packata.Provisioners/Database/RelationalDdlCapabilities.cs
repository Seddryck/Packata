using DubUrl.Querying.Dialects;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;

namespace Packata.Provisioners.Database;

/// <summary>Offline engine restrictions, separate from DubUrl renderer availability and live connection capabilities.</summary>
internal sealed record RelationalDdlCapabilities(bool Tables = true, bool Unique = true,
    bool ForeignKeys = true, bool Checks = true, bool EnforcesKeys = true)
{
    internal static RelationalDdlCapabilities For(IDialect dialect) => dialect switch
    {
        DubUrl.Provider.Drill.Sql.DrillDialect or DubUrl.Provider.PostgreSql.Sql.QuestDbDialect => new(Tables: false),
        DubUrl.Provider.PostgreSql.Sql.CrateDbDialect => new(Unique: false, ForeignKeys: false),
        DubUrl.Provider.MySql.Sql.SingleStoreDialect => new(ForeignKeys: false, Checks: false),
        DubUrl.Provider.Snowflake.Sql.SnowflakeDialect => new(EnforcesKeys: false),
        _ => new()
    };

    internal static bool IsRedundantUnique(DataSchema schema, DataField field, ContractProvisioningOptions options)
        => options.Constraints.HasFlag(ContractConstraintOptions.PrimaryKey) && schema.PrimaryKey.Count == 1
            && (schema.PrimaryKey[0] == field.Name || schema.PrimaryKey[0] == field.PhysicalName);

    internal ProvisioningDiagnostic[] Validate(IDialect dialect, IReadOnlyList<DataAsset> assets, ContractProvisioningOptions options)
    {
        if (!Tables) return [new("PROV009", "*", $"Dialect '{dialect.GetType().Name}' is disabled for relational DDL generation.")];
        var diagnostics = new List<ProvisioningDiagnostic>();
        foreach (var asset in assets)
            ValidateAsset(dialect, asset, options, diagnostics);
        return diagnostics.ToArray();
    }

    private void ValidateAsset(IDialect dialect, DataAsset asset, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var schema = asset.Schema ?? throw new InvalidOperationException($"Asset '{asset.Id}' requires a schema.");
        CheckKey(dialect, asset.Id, options, diagnostics, (ContractConstraintOptions.PrimaryKey, schema.PrimaryKey.Count > 0, true, "PRIMARY KEY"));
        CheckKey(dialect, asset.Id, options, diagnostics, (ContractConstraintOptions.Unique, HasUnique(schema, options), Unique, "UNIQUE"));
        CheckKey(dialect, asset.Id, options, diagnostics, (ContractConstraintOptions.ForeignKeys,
            schema.Relationships.Any(r => r.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)), ForeignKeys, "FOREIGN KEY"));
        if (!Checks && options.Constraints.HasFlag(ContractConstraintOptions.Checks)
            && schema.Fields.Any(f => f.Constraints.Any(c => IsCheck(c.Kind))))
            diagnostics.Add(new("PROV009", asset.Id,
                $"Dialect '{dialect.GetType().Name}' does not support the requested CHECK constraints. Disable Checks explicitly or choose another dialect; no SQL was generated."));
    }

    private static bool HasUnique(DataSchema schema, ContractProvisioningOptions options)
        => schema.Fields.Any(field => field.Constraints.Any(c => c.Kind.Equals("unique", StringComparison.OrdinalIgnoreCase)
            && c.Value is true) && !IsRedundantUnique(schema, field, options));

    private void CheckKey(IDialect dialect, string assetId, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics, (ContractConstraintOptions Option, bool Present, bool Supported, string Name) key)
    {
        if (!key.Present || !options.Constraints.HasFlag(key.Option)) return;
        if (!key.Supported) diagnostics.Add(new("PROV009", assetId,
            $"Dialect '{dialect.GetType().Name}' cannot enforce the requested {key.Name} constraint. Disable that constraint explicitly or choose another dialect; no SQL was generated."));
        else if (!EnforcesKeys) diagnostics.Add(new("PROV010", assetId,
            $"{key.Name} is declared as metadata but is not enforced on Snowflake standard tables. NOT NULL and CHECK constraints are enforced."));
    }

    private static bool IsCheck(string kind) => kind.ToLowerInvariant() is
        "minimum" or "maximum" or "exclusiveminimum" or "exclusivemaximum" or "minlength" or "maxlength" or "enum" or "pattern";
}
