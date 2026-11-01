using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
namespace Packata.Provisioners.Database;
/// <summary>Pure metadata planning; no connection, capability probe, credential source, reader or deployer.</summary>
public sealed class RelationalSchemaPlanner(IDialect dialect, SchemaScriptRenderer? renderer = null, DbTypeMapper? mapper = null)
{
    private IDialect Dialect { get; } = dialect;
    private SchemaScriptRenderer ScriptRenderer { get; } = renderer ?? new(dialect);
    private DbTypeMapper DbTypeMapper { get; } = mapper ?? new();
    public IReadOnlyList<ProvisioningDiagnostic> ValidateCapabilities(IReadOnlyList<DataAsset> assets,
        ContractProvisioningOptions? options = null)
        => RelationalDdlCapabilities.For(Dialect).Validate(Dialect, assets, options ?? new());
    private sealed record ColumnMappingContext(DataAsset Asset, ContractProvisioningOptions Options,
        bool HasPrimaryKey, bool PrimaryKeyIsValid, IReadOnlyCollection<string> PrimaryKeyColumns,
        ICollection<ProvisioningDiagnostic> Diagnostics);

    private sealed record TableConstraintMappingContext(DataAsset Asset, DataContract Contract,
        IReadOnlyDictionary<DataAsset, DatabaseObjectName> Identities, ContractProvisioningOptions Options,
        bool HasPrimaryKey, bool PrimaryKeyIsValid, string[] PrimaryKeyColumns);

    public SchemaProvisioningPlan PlanSchema(DataContract contract,
        ContractProvisioningOptions? options = null, string? assetId = null, CancellationToken cancellationToken = default,
        IReadOnlyList<DataAsset>? referencedAssets = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        var assets = assetId is null ? contract.Assets : contract.Assets.Where(a => a.Id == assetId).ToArray();
        if (assetId is not null && assets.Count != 1) throw new ArgumentException("Select an unambiguous eligible asset ID.");
        options ??= new();
        var diagnostics = new List<ProvisioningDiagnostic>();
        diagnostics.AddRange(ValidateCapabilities(assets, options));
        if (diagnostics.Any(d => d.Code == "PROV009")) return new(string.Empty, diagnostics);
        ReportCommentSupport(contract, options, diagnostics);
        var metadata = contract with { Assets = [.. assets, .. referencedAssets ?? []] };
        var identities = ResolveIdentities(metadata, metadata.Assets, diagnostics, cancellationToken);
        var outputTables = assets.Where(identities.ContainsKey)
            .Select(asset => Map(asset, identities[asset], metadata, identities, options, diagnostics).Build()).ToArray();
        var selected = outputTables.Select(t => t.Identity.Key).ToHashSet(StringComparer.Ordinal);
        var referenceTables = ReferenceTables(referencedAssets ?? [], identities, new HashSet<string>(selected, StringComparer.Ordinal), diagnostics);
        var schema = new Schema(outputTables, [], [], referenceTables);
        cancellationToken.ThrowIfCancellationRequested();
        string script;
        try { script = ScriptRenderer.Render(schema); }
        catch (Exception error) when (error is NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new("PROV009", "*", error.Message));
            return new(string.Empty, diagnostics);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(script, diagnostics.ToArray());
    }

    private Table[] ReferenceTables(IReadOnlyList<DataAsset> assets,
        IReadOnlyDictionary<DataAsset, DatabaseObjectName> identities, HashSet<string> selected,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var tables = new List<Table>();
        foreach (var asset in assets)
        {
            if (!identities.TryGetValue(asset, out var identity) || !selected.Add(identity.Key)) continue;
            // DubUrl validates key types at both endpoints; external tables are never rendered.
            tables.Add(new TableBuilder().WithIdentity(identity).WithColumns(columns =>
            {
                foreach (var field in asset.Schema!.Fields)
                    columns.Add(c => MapStorageColumn(c, asset, field, diagnostics));
                return columns;
            }).Build());
        }
        return tables.ToArray();
    }

    private void ReportCommentSupport(DataContract contract, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (options.Descriptions && !ScriptRenderer.SupportsComments
            && contract.Assets.Any(asset => !string.IsNullOrWhiteSpace(asset.Description)
                || asset.Schema?.Fields.Any(field => !string.IsNullOrWhiteSpace(field.Description)) == true))
            diagnostics.Add(ScriptRenderer.EngineSupportsComments
                ? new("PROV006", "*", $"The current DubUrl renderer does not implement database comments for dialect '{Dialect.GetType().Name}'; descriptions were omitted.")
                : new("PROV012", "*", $"Dialect '{Dialect.GetType().Name}' cannot express database comments; descriptions were omitted."));
    }

    private Dictionary<DataAsset, DatabaseObjectName> ResolveIdentities(DataContract contract,
        IReadOnlyList<DataAsset> assets, ICollection<ProvisioningDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var resolver = new RelationalObjectNameResolver(Dialect, contract);
        var identities = new Dictionary<DataAsset, DatabaseObjectName>();
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidatePhysicalColumns(asset, diagnostics)) continue;
            identities.Add(asset, resolver.Resolve(asset, diagnostics));
        }
        return identities;
    }

    internal ITableBuilder Map(DataAsset asset, DatabaseObjectName tableName, DataContract contract,
        IReadOnlyDictionary<DataAsset, DatabaseObjectName> identities, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var schema = asset.Schema ?? throw new InvalidOperationException($"Asset '{asset.Id}' requires a schema.");
        var hasPrimaryKey = options.Constraints.HasFlag(ContractConstraintOptions.PrimaryKey)
            && schema.PrimaryKey.Count > 0;
        string[] primaryKeyColumns = [];
        string? primaryKeyError = null;
        var primaryKeyIsValid = !hasPrimaryKey
            || TryColumns(schema, schema.PrimaryKey, out primaryKeyColumns, out primaryKeyError);
        if (!primaryKeyIsValid)
            diagnostics.Add(new("PROV002", asset.Id, primaryKeyError!));

        var columnContext = new ColumnMappingContext(asset, options, hasPrimaryKey,
            primaryKeyIsValid, primaryKeyColumns, diagnostics);
        var constraintContext = new TableConstraintMappingContext(asset, contract, identities, options,
            hasPrimaryKey, primaryKeyIsValid, primaryKeyColumns);
        var table = new TableBuilder().WithIdentity(tableName)
            .WithColumns(columns => AddColumns(columns, columnContext))
            .WithConstraints(constraints => AddTableConstraints(constraints, constraintContext, diagnostics));
        return options.Descriptions && ScriptRenderer.SupportsComments
            ? table.WithDescription(asset.Description)
            : table;
    }

    private ColumnCollectionBuilder AddColumns(ColumnCollectionBuilder columns, ColumnMappingContext context)
    {
        foreach (var field in context.Asset.Schema!.Fields)
            columns.Add(column => MapColumn(column, field, context));
        return columns;
    }

    private IColumnBuilder MapColumn(ColumnBuilder column, DataField field, ColumnMappingContext context)
    {
        var constraints = MapStorageColumn(column, context.Asset, field, context.Diagnostics);
        ApplyColumnConstraints(constraints, field, context.Options, context.HasPrimaryKey,
            context.PrimaryKeyIsValid, context.PrimaryKeyColumns);
        if (context.Options.Descriptions && ScriptRenderer.SupportsComments)
            constraints.WithDescription(field.Description);
        if (context.Options.Constraints.HasFlag(ContractConstraintOptions.Checks))
            AddChecks(column, constraints, context.Asset, field, context.Diagnostics);
        return column;
    }

    private IColumnConstraintBuilder MapStorageColumn(ColumnBuilder column, DataAsset asset, DataField field,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var logicalType = DbTypeMapper.Resolve(field, TypeMappingOptions.LegacyCompatibility).DatabaseType!.Value;
        var builder = column.WithName(field.PhysicalName ?? field.Name).WithType(logicalType);
        ApplyStringDimensions(builder, field, logicalType);
        return ApplyPhysicalType(builder, asset, field, diagnostics);
    }

    private void ApplyStringDimensions(IColumnTypeSettingsBuilder builder, DataField field, System.Data.DbType type)
    {
        if (!NeedsStringDimensions(field, type)) return;
        if (DeclaredMaximumLength(field) is int length) builder.WithLength(length);
        else if (UsesUnboundedText(field, type))
            builder.WithLength(VarLengthColumn.Unbounded);
    }

    private bool NeedsStringDimensions(DataField field, System.Data.DbType type)
    {
        if (!IsString(type) || !UsesStringDimensions()) return false;
        return !PhysicalTypeDefinition.TryParse(field.PhysicalType, out var physical)
            || physical is { Length: null, Precision: null };
    }

    private static int? DeclaredMaximumLength(DataField field)
    {
        var maximum = field.Constraints.FirstOrDefault(c => c.Kind.Equals("maxLength", StringComparison.OrdinalIgnoreCase))?.Value;
        return long.TryParse(Convert.ToString(maximum, System.Globalization.CultureInfo.InvariantCulture), out var length)
            && length is > 0 and <= int.MaxValue ? (int)length : null;
    }

    private bool UsesUnboundedText(DataField field, System.Data.DbType type)
        => Dialect is DubUrl.Provider.SqlServer.Sql.TSqlDialect && field.PhysicalType is null
            && type is System.Data.DbType.String or System.Data.DbType.AnsiString;

    private static bool IsString(System.Data.DbType type) => type is System.Data.DbType.String or System.Data.DbType.AnsiString
        or System.Data.DbType.StringFixedLength or System.Data.DbType.AnsiStringFixedLength;

    private bool UsesStringDimensions() => Dialect is DubUrl.Provider.SqlServer.Sql.TSqlDialect
        or DubUrl.Provider.MySql.Sql.MySqlDialect or DubUrl.Provider.MySql.Sql.SingleStoreDialect
        or DubUrl.Provider.Teradata.Sql.TeradataDialect;

    private IColumnConstraintBuilder ApplyPhysicalType(IColumnTypeSettingsBuilder typeBuilder, DataAsset asset,
        DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (field.PhysicalType is null) return typeBuilder;
        if (!PhysicalTypeDefinition.TryParse(field.PhysicalType, out var physicalType))
        {
            diagnostics.Add(new("PROV005", asset.Id,
                $"Physical type '{field.PhysicalType}' on field '{field.Name}' is unsupported; logical type mapping was used."));
            return typeBuilder;
        }
        var constraints = ApplyPhysicalDimensions(typeBuilder, physicalType!);
        if (!NativeTypeRegistry.Default.TryResolve(Dialect, physicalType!.NativeType, out _))
            diagnostics.Add(new("PROV005", asset.Id,
                $"Physical type '{field.PhysicalType}' on field '{field.Name}' is not supported by target dialect '{Dialect.GetType().Name}'; logical type mapping was used."));
        constraints.WithNativeType(physicalType.NativeType, NativeTypeFallback.LogicalType);
        return constraints;
    }

    private static IColumnConstraintBuilder ApplyPhysicalDimensions(IColumnTypeSettingsBuilder builder,
        PhysicalTypeDefinition physicalType) => physicalType switch
        {
            { Precision: int precision, Scale: int scale } => builder.WithPrecision(precision).WithScale(scale),
            { Length: int length } => builder.WithLength(length),
            _ => builder
        };

    private static void ApplyColumnConstraints(IColumnConstraintBuilder builder, DataField field,
        ContractProvisioningOptions options, bool hasPrimaryKey, bool primaryKeyIsValid,
        IReadOnlyCollection<string> primaryKeyColumns)
    {
        if (ConstraintBoolean(field, "unique") && options.Constraints.HasFlag(ContractConstraintOptions.Unique)) builder.WithUnique();
        var isPrimaryKey = primaryKeyIsValid && hasPrimaryKey
            && primaryKeyColumns.Contains(field.PhysicalName ?? field.Name, StringComparer.Ordinal);
        if (isPrimaryKey
            || (!field.Nullable && options.Constraints.HasFlag(ContractConstraintOptions.Required)))
            builder.WithNotNullable();
        else
            builder.WithNullable();
    }

    private void AddChecks(ColumnBuilder column, IColumnConstraintBuilder builder, DataAsset asset,
        DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        foreach (var constraint in field.Constraints.Where(value => value.Kind != "unique" && !IsTablePrimaryKey(value, asset, field)))
        {
            if (IsStorageLengthBound(column, constraint)) continue;
            var check = MapCheck(column, constraint, asset, field, diagnostics);
            if (check is not null) builder.WithCheck(_ => check);
            else if (!IsSpecialCheck(constraint))
                diagnostics.Add(new("PROV004", asset.Id,
                    $"Constraint '{constraint.Kind}' on field '{field.Name}' is not supported."));
        }
    }

    private bool IsStorageLengthBound(ColumnBuilder builder, DataConstraint constraint)
    {
        if (!TryMaximumLength(constraint, out var maximum)
            || Dialect is DubUrl.Provider.Sqlite.Sql.SqliteDialect or DubUrl.Provider.DuckDb.Sql.DuckDbDialect)
            return false;
        var column = ((IColumnBuilder)builder).Build();
        if (column is not VarLengthColumn { Length: > 0 } bounded || !IsString(column.Type)) return false;
        return StorageEnforcesMaximum(column, bounded.Length, maximum);
    }

    private bool StorageEnforcesMaximum(Column column, int length, long maximum)
    {
        string rendered;
        try { rendered = RenderStorageType(column, length); }
        catch (Exception error) when (error is NotSupportedException or ArgumentException)
        {
            // Keep validation in the final renderer, which reports PROV009 without partial SQL.
            return false;
        }
        return PhysicalTypeDefinition.TryParse(rendered, out var storage)
            && storage is { Length: > 0 } && storage.Length <= maximum
            && IsBoundedCharacterType(storage.NativeType.Name);
    }

    private static bool TryMaximumLength(DataConstraint constraint, out long maximum)
    {
        maximum = 0;
        return constraint.Kind.Equals("maxLength", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(Convert.ToString(constraint.Value, System.Globalization.CultureInfo.InvariantCulture), out maximum)
            && maximum >= 0;
    }

    private string RenderStorageType(Column column, int length)
    {
        var resolved = string.Empty;
        var native = column.NativeType is not null
            && NativeTypeRegistry.Default.TryResolve(Dialect, column.NativeType, out resolved);
        var type = native
            ? resolved
            : Dialect.DbTypeMapper.ToDictionary()[column.Type.ToString()].ToString()!;
        // Ask the provider for the actual storage type: TEXT and MAX are not bounded,
        // and some providers normalize bounded logical TEXT to VARCHAR.
        return Dialect.SchemaSqlRenderer.RenderColumnType(Dialect, type, length, 0, 0, native);
    }

    private static bool IsBoundedCharacterType(string type) => type.ToLowerInvariant()
        is "varchar" or "nvarchar" or "char" or "nchar" or "character varying" or "character" or "varchar2" or "nvarchar2";

    private static bool IsTablePrimaryKey(DataConstraint constraint, DataAsset asset, DataField field)
        => constraint.Kind.Equals("primaryKey", StringComparison.OrdinalIgnoreCase) && constraint.Value is true
            && asset.Schema!.PrimaryKey.Any(name => name == field.Name || name == field.PhysicalName);

    private static bool IsSpecialCheck(DataConstraint constraint) =>
        constraint.Kind.Equals("pattern", StringComparison.OrdinalIgnoreCase)
        || constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase);

    private static TableConstraintCollectionBuilder AddTableConstraints(TableConstraintCollectionBuilder constraints,
        TableConstraintMappingContext context, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (context.HasPrimaryKey && context.PrimaryKeyIsValid)
            constraints.AddPrimaryKey(primaryKey => primaryKey.WithColumnNames(context.PrimaryKeyColumns));
        if (context.Options.Constraints.HasFlag(ContractConstraintOptions.ForeignKeys))
            AddForeignKeys(constraints, context, diagnostics);
        return constraints;
    }

    private static void AddForeignKeys(TableConstraintCollectionBuilder constraints,
        TableConstraintMappingContext context,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        for (var index = 0; index < context.Asset.Schema!.Relationships.Count; index++)
        {
            var relationship = context.Asset.Schema.Relationships[index];
            if (!relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryResolveForeignKey(context, relationship, out var target,
                out var targetIdentity, out var sourceColumns, out var targetColumns, out var error))
            {
                diagnostics.Add(new("PROV003", context.Asset.Id, error));
                continue;
            }
            var targetName = target!.Id.Contains("#/schema/", StringComparison.Ordinal) ? target.Name : target.Id;
            var name = relationship.Name ?? $"FK_{context.Asset.Id}_{targetName}_{index + 1}";
            constraints.AddForeignKey(foreignKey => foreignKey.WithName(name)
                .FromColumns(sourceColumns).References(targetIdentity!, targetColumns));
        }
    }

    private static bool TryResolveForeignKey(TableConstraintMappingContext context, DataRelationship relationship,
        out DataAsset? target, out DatabaseObjectName? targetIdentity, out string[] sourceColumns,
        out string[] targetColumns, out string error)
    {
        target = context.Contract.Assets.FirstOrDefault(value => value.Id == relationship.TargetAsset
            || value.Name == relationship.TargetAsset);
        targetIdentity = null;
        sourceColumns = [];
        targetColumns = [];
        if (target?.Schema is null || !context.Identities.TryGetValue(target, out targetIdentity))
        {
            error = $"Relationship to '{relationship.TargetAsset}' does not resolve to a provisioned table.";
            return false;
        }
        if (!TryColumns(context.Asset.Schema!, relationship.Fields, out sourceColumns, out var sourceError))
        {
            error = sourceError!;
            return false;
        }
        if (!TryColumns(target.Schema, relationship.TargetFields, out targetColumns, out var targetError))
        {
            error = targetError!;
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool ConstraintBoolean(DataField field, string kind) =>
        field.Constraints.FirstOrDefault(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))?.Value is true;

    private ICheckBuildable? MapCheck(IColumnName column, DataConstraint constraint, DataAsset asset,
        DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (constraint.Kind.Equals("pattern", StringComparison.OrdinalIgnoreCase))
            return MapPatternCheck(column, constraint, asset, field, diagnostics);
        if (constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase))
            return MapMembershipCheck(column, constraint, asset, field, diagnostics);
        return MapComparisonCheck(column, constraint);
    }

    private ICheckBuildable? MapPatternCheck(IColumnName column, DataConstraint constraint, DataAsset asset,
        DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (constraint.Value is string pattern
            && (Dialect.Capabilities ?? DialectCapabilities.Default).SupportsRegularExpressionChecks)
            return ((ICheckBuilder)new CheckBuilder(column)).WithRegex(
                expression => expression.WithCurrentColumn(), pattern,
                field.Required ? RegexNullBehavior.RejectNull : RegexNullBehavior.AllowNull);
        diagnostics.Add(new("PROV004", asset.Id,
            $"Constraint 'pattern' on field '{field.Name}' is not supported by target dialect '{Dialect.GetType().Name}'."));
        return null;
    }

    private static ICheckBuildable? MapMembershipCheck(IColumnName column, DataConstraint constraint,
        DataAsset asset, DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (!TryMembershipValues(constraint.Value, out var values, out var error))
        {
            diagnostics.Add(new("PROV004", asset.Id,
                $"Constraint 'enum' on field '{field.Name}' {error}."));
            return null;
        }
        var nullBehavior = values.Any(value => value is null)
            ? NullMembershipBehavior.MatchNull
            : NullMembershipBehavior.SqlThreeValuedLogic;
        return ((ICheckBuilder)new CheckBuilder(column)).WithMembership(
            expression => expression.WithCurrentColumn(), values, nullBehavior);
    }

    private static ICheckBuildable? MapComparisonCheck(IColumnName column, DataConstraint constraint)
    {
        var (op, length) = constraint.Kind.ToLowerInvariant() switch
        {
            "minimum" => (">=", false), "maximum" => ("<=", false),
            "exclusiveminimum" => (">", false), "exclusivemaximum" => ("<", false),
            "minlength" => (">=", true), "maxlength" => ("<=", true),
            _ => (null, false)
        };
        if (op is null || constraint.Value is null) return null;
        return ((ICheckBuilder)new CheckBuilder(column)).WithComparison(
            left => length ? left.WithFunctionCurrentColumn("Length") : left.WithCurrentColumn(),
            op, right => right.WithValue(constraint.Value));
    }

    private static bool TryMembershipValues(object? value, out object?[] values, out string error)
    {
        values = [];
        if (value is not System.Collections.IEnumerable sequence || value is string)
        {
            error = "does not contain a value collection";
            return false;
        }
        values = sequence.Cast<object?>().ToArray();
        if (values.Length == 0)
        {
            error = "has no values";
            return false;
        }
        var types = values.Where(item => item is not null).Select(item => item!.GetType()).Distinct().ToArray();
        if (types.Length > 1)
        {
            error = "contains incompatible value types";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryColumns(DataSchema schema, IReadOnlyList<string> names,
        out string[] columns, out string? error)
    {
        var resolved = new List<string>();
        foreach (var name in names)
        {
            var matches = schema.Fields.Where(field => field.Name == name).ToArray();
            if (matches.Length == 0)
                matches = schema.Fields.Where(field => field.PhysicalName == name).ToArray();
            if (matches.Length != 1)
            {
                columns = [];
                error = matches.Length == 0
                    ? $"Field reference '{name}' does not resolve to a column."
                    : $"Field reference '{name}' is ambiguous.";
                return false;
            }
            resolved.Add(matches[0].PhysicalName ?? matches[0].Name);
        }
        if (resolved.Count == 0 || resolved.Distinct(StringComparer.Ordinal).Count() != resolved.Count)
        {
            columns = [];
            error = resolved.Count == 0
                ? "At least one field reference is required."
                : "Field references resolve to duplicate physical columns.";
            return false;
        }
        columns = resolved.ToArray();
        error = null;
        return true;
    }

    private static bool ValidatePhysicalColumns(DataAsset asset,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (asset.Schema is null)
            throw new InvalidOperationException($"Asset '{asset.Id}' requires a schema.");
        var duplicates = asset.Schema.Fields.GroupBy(field => field.PhysicalName ?? field.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length == 0) return true;
        diagnostics.Add(new("PROV008", asset.Id,
            $"Duplicate physical column name(s) {string.Join(", ", duplicates)}; the table was not provisioned."));
        return false;
    }
}
