using DubUrl;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Core.Reading;

namespace Packata.Provisioners.Database;

public class DubUrlProvisioner : IDataContractProvisioner
{
    protected SchemaScriptRenderer ScriptRenderer { get; }
    protected SchemaScriptDeployer ScriptDeployer { get; }
    protected DbTypeMapper DbTypeMapper { get; }
    protected BulkCopyEngineFactory BulkCopyEngineFactory { get; }
    protected IDataEndpointReaderFactory? ReaderFactory { get; }
    protected IDialect Dialect { get; }
    public ConnectionUrl ConnectionUrl { get; }

    protected internal DubUrlProvisioner(ConnectionUrl connectionUrl,
        SchemaScriptRenderer? scriptRenderer = null, SchemaScriptDeployer? deployer = null,
        DbTypeMapper? dbTypeMapper = null, BulkCopyEngineFactory? bulkCopyEngineFactory = null,
        IDataEndpointReaderFactory? readerFactory = null)
    {
        ConnectionUrl = connectionUrl;
        Dialect = GetDialect(connectionUrl);
        ScriptRenderer = scriptRenderer ?? new(Dialect, SchemaCreationOptions.None);
        ScriptDeployer = deployer ?? new();
        DbTypeMapper = dbTypeMapper ?? new();
        BulkCopyEngineFactory = bulkCopyEngineFactory ?? new();
        ReaderFactory = readerFactory;
    }

    public DubUrlProvisioner(ConnectionUrl connectionUrl)
        : this(connectionUrl, new(GetDialect(connectionUrl)), new(), new()) { }

    private static IDialect GetDialect(ConnectionUrl connectionUrl)
    {
        try { return connectionUrl.Dialect; }
        catch
        {
            var builder = new DialectRegistryBuilder(); builder.AddDialect<AnsiDialect>(["ansi"]);
            return builder.Build().Get<AnsiDialect>();
        }
    }

    public IReadOnlyList<ProvisioningDiagnostic> DeploySchema(DataContract contract,
        ContractProvisioningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        options ??= new();
        var diagnostics = new List<ProvisioningDiagnostic>();
        if (options.Descriptions && !ScriptRenderer.SupportsComments
            && contract.Assets.Any(asset => !string.IsNullOrWhiteSpace(asset.Description)
                || asset.Schema?.Fields.Any(field => !string.IsNullOrWhiteSpace(field.Description)) == true))
            diagnostics.Add(new("PROV006", "*",
                $"Database comments are not supported by target dialect '{Dialect.GetType().Name}'."));

        var resolver = new RelationalObjectNameResolver(Dialect, contract);
        var identities = new Dictionary<DataAsset, DatabaseObjectName>();
        foreach (var asset in contract.Assets)
        {
            if (!ValidatePhysicalColumns(asset, diagnostics)) continue;
            identities.Add(asset, resolver.Resolve(asset, diagnostics));
        }
        var schema = new SchemaBuilder().WithTables(tables =>
        {
            foreach (var pair in identities)
                tables.Add(Map(pair.Key, pair.Value, contract, identities, options, diagnostics));
            return tables;
        }).Build();
        ScriptDeployer.DeploySchema(ConnectionUrl, ScriptRenderer.Render(schema));
        return diagnostics;
    }

    public async Task<IReadOnlyList<ProvisioningDiagnostic>> LoadDataAsync(DataContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var readerFactory = ReaderFactory ?? throw new InvalidOperationException(
            "A canonical endpoint reader factory is required to load data.");
        var diagnostics = new List<ProvisioningDiagnostic>();
        var resolver = new RelationalObjectNameResolver(Dialect, contract);
        using var bulkCopy = BulkCopyEngineFactory.Create(ConnectionUrl);
        foreach (var asset in contract.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = asset.EndpointBindings.FirstOrDefault();
            var endpoint = binding is null ? null : contract.Endpoints.FirstOrDefault(x => x.Id == binding.EndpointId);
            if (endpoint is null)
            {
                diagnostics.Add(new("PROV001", asset.Id,
                    "No resolvable endpoint binding is available; data loading was skipped."));
                continue;
            }
            var request = new DataEndpointReadRequest(endpoint, asset.Schema, binding!.AssetPath);
            using var reader = await readerFactory.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            var identity = resolver.Resolve(asset, diagnostics);
            bulkCopy.Write(Dialect.SchemaSqlRenderer.RenderObjectName(
                Dialect, identity.Catalog, identity.Database, identity.Schema, identity.Name), reader);
        }
        return diagnostics;
    }

    public async Task<IReadOnlyList<ProvisioningDiagnostic>> ExecuteAsync(DataContract contract,
        ContractProvisioningOptions? options = null, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ProvisioningDiagnostic>(DeploySchema(contract, options));
        diagnostics.AddRange(await LoadDataAsync(contract, cancellationToken).ConfigureAwait(false));
        return diagnostics;
    }

    protected internal ITableBuilder Map(DataAsset asset, DatabaseObjectName tableName, DataContract contract,
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

        var table = new TableBuilder().WithIdentity(tableName).WithColumns(columns =>
        {
            foreach (var field in schema.Fields)
            {
                columns.Add(column =>
                {
                    var logicalType = DbTypeMapper.Map(field.LogicalType, field.Format);
                    var typeBuilder = column.WithName(field.PhysicalName ?? field.Name).WithType(logicalType);
                    IColumnConstraintBuilder constraintsBuilder = typeBuilder;
                    PhysicalTypeDefinition? physicalType = null;
                    if (field.PhysicalType is not null
                        && !PhysicalTypeDefinition.TryParse(field.PhysicalType, out physicalType))
                    {
                        diagnostics.Add(new("PROV005", asset.Id,
                            $"Physical type '{field.PhysicalType}' on field '{field.Name}' is unsupported; logical type mapping was used."));
                    }
                    else if (physicalType is not null)
                    {
                        constraintsBuilder = physicalType switch
                        {
                            { Precision: int precision, Scale: int scale }
                                => typeBuilder.WithPrecision(precision).WithScale(scale),
                            { Length: int length } => typeBuilder.WithLength(length),
                            _ => typeBuilder
                        };
                        if (!NativeTypeRegistry.Default.TryResolve(Dialect, physicalType.NativeType, out _))
                            diagnostics.Add(new("PROV005", asset.Id,
                                $"Physical type '{field.PhysicalType}' on field '{field.Name}' is not supported by target dialect '{Dialect.GetType().Name}'; logical type mapping was used."));
                        constraintsBuilder.WithNativeType(physicalType.NativeType, NativeTypeFallback.LogicalType);
                    }

                    if (ConstraintBoolean(field, "unique")
                        && options.Constraints.HasFlag(ContractConstraintOptions.Unique))
                        constraintsBuilder.WithUnique();
                    var isPrimaryKey = primaryKeyIsValid && hasPrimaryKey
                        && primaryKeyColumns.Contains(field.PhysicalName ?? field.Name, StringComparer.Ordinal);
                    if (isPrimaryKey || field.Required
                        && options.Constraints.HasFlag(ContractConstraintOptions.Required))
                        constraintsBuilder.WithNotNullable();
                    else
                        constraintsBuilder.WithNullable();
                    if (options.Descriptions && ScriptRenderer.SupportsComments)
                        constraintsBuilder.WithDescription(field.Description);
                    if (options.Constraints.HasFlag(ContractConstraintOptions.Checks))
                    {
                        foreach (var constraint in field.Constraints.Where(x => x.Kind != "unique"))
                        {
                            var check = MapCheck(column, constraint, asset, field, diagnostics);
                            if (check is null)
                            {
                                if (!constraint.Kind.Equals("pattern", StringComparison.OrdinalIgnoreCase)
                                    && !constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase))
                                    diagnostics.Add(new("PROV004", asset.Id,
                                        $"Constraint '{constraint.Kind}' on field '{field.Name}' is not supported."));
                            }
                            else ((IColumnConstraintBuilder)column).WithCheck(_ => check);
                        }
                    }
                    return column;
                });
            }
            return columns;
        }).WithConstraints(constraints =>
        {
            if (hasPrimaryKey && primaryKeyIsValid)
                constraints.AddPrimaryKey(primaryKey => primaryKey.WithColumnNames(primaryKeyColumns));

            if (options.Constraints.HasFlag(ContractConstraintOptions.ForeignKeys))
            {
                for (var index = 0; index < schema.Relationships.Count; index++)
                {
                    var relationship = schema.Relationships[index];
                    if (!relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = contract.Assets.FirstOrDefault(value => value.Id == relationship.TargetAsset
                        || value.Name == relationship.TargetAsset);
                    string? sourceError = null;
                    string? targetError = null;
                    string[] sourceColumns = [];
                    string[] targetColumns = [];
                    if (target?.Schema is null || !identities.TryGetValue(target, out var targetIdentity)
                        || !TryColumns(schema, relationship.Fields, out sourceColumns, out sourceError)
                        || !TryColumns(target.Schema, relationship.TargetFields, out targetColumns, out targetError))
                    {
                        diagnostics.Add(new("PROV003", asset.Id,
                            sourceError ?? targetError
                            ?? $"Relationship to '{relationship.TargetAsset}' does not resolve to a provisioned table."));
                        continue;
                    }
                    var name = relationship.Name ?? $"FK_{asset.Id}_{target.Id}_{index + 1}";
                    constraints.AddForeignKey(foreignKey => foreignKey.WithName(name)
                        .FromColumns(sourceColumns).References(targetIdentity, targetColumns));
                }
            }
            return constraints;
        });
        return options.Descriptions && ScriptRenderer.SupportsComments
            ? table.WithDescription(asset.Description)
            : table;
    }

    private static bool ConstraintBoolean(DataField field, string kind) =>
        field.Constraints.FirstOrDefault(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))?.Value is true;

    private ICheckBuildable? MapCheck(IColumnName column, DataConstraint constraint, DataAsset asset,
        DataField field, ICollection<ProvisioningDiagnostic> diagnostics)
    {
        if (constraint.Kind.Equals("pattern", StringComparison.OrdinalIgnoreCase))
        {
            if (constraint.Value is not string pattern
                || !(Dialect.Capabilities ?? DialectCapabilities.Default).SupportsRegularExpressionChecks)
            {
                diagnostics.Add(new("PROV004", asset.Id,
                    $"Constraint 'pattern' on field '{field.Name}' is not supported by target dialect '{Dialect.GetType().Name}'."));
                return null;
            }
            return ((ICheckBuilder)new CheckBuilder(column)).WithRegex(
                expression => expression.WithCurrentColumn(), pattern,
                field.Required ? RegexNullBehavior.RejectNull : RegexNullBehavior.AllowNull);
        }
        if (constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase))
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
