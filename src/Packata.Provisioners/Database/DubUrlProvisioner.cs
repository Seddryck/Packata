using DubUrl;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using DubUrl.Schema.Builders;
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
    public ConnectionUrl ConnectionUrl { get; }

    protected internal DubUrlProvisioner(ConnectionUrl connectionUrl,
        SchemaScriptRenderer? scriptRenderer = null, SchemaScriptDeployer? deployer = null,
        DbTypeMapper? dbTypeMapper = null, BulkCopyEngineFactory? bulkCopyEngineFactory = null,
        IDataEndpointReaderFactory? readerFactory = null)
    {
        ConnectionUrl = connectionUrl;
        ScriptRenderer = scriptRenderer ?? new(connectionUrl.Dialect, SchemaCreationOptions.None);
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
        var schema = new SchemaBuilder().WithTables(tables =>
        {
            foreach (var asset in contract.Assets) tables.Add(Map(asset, options, diagnostics));
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
            using var reader = await readerFactory.OpenAsync(endpoint, asset.Schema, cancellationToken).ConfigureAwait(false);
            bulkCopy.Write(asset.PhysicalName ?? asset.Name, reader);
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

    protected internal ITableBuilder Map(DataAsset asset, ContractProvisioningOptions options,
        ICollection<ProvisioningDiagnostic> diagnostics)
    {
        var schema = asset.Schema ?? throw new InvalidOperationException($"Asset '{asset.Id}' requires a schema.");
        if (schema.PrimaryKey.Count > 1)
            diagnostics.Add(new("PROV002", asset.Id, "Composite primary keys are not supported by this provisioner."));
        foreach (var relationship in schema.Relationships)
            diagnostics.Add(new("PROV003", asset.Id,
                $"Relationship to '{relationship.TargetAsset}' is not supported by this provisioner."));

        return new TableBuilder().WithName(asset.PhysicalName ?? asset.Name).WithColumns(columns =>
        {
            foreach (var field in schema.Fields)
            {
                columns.Add(column =>
                {
                    column.WithName(field.Name)
                        .WithType(DbTypeMapper.Map(field.LogicalType, field.Format))
                        .WithPrimaryKeyIf(schema.PrimaryKey.Count == 1 && schema.PrimaryKey.Contains(field.Name)
                            && options.Constraints.HasFlag(ContractConstraintOptions.PrimaryKey))
                        .WithUniqueIf(ConstraintBoolean(field, "unique")
                            && options.Constraints.HasFlag(ContractConstraintOptions.Unique))
                        .WithNullableIf(!field.Required
                            && options.Constraints.HasFlag(ContractConstraintOptions.Required));
                    if (options.Constraints.HasFlag(ContractConstraintOptions.Checks))
                    {
                        foreach (var constraint in field.Constraints.Where(x => x.Kind != "unique"))
                        {
                            var check = MapCheck(column, constraint);
                            if (check is null)
                                diagnostics.Add(new("PROV004", asset.Id,
                                    $"Constraint '{constraint.Kind}' on field '{field.Name}' is not supported."));
                            else ((IColumnConstraintBuilder)column).WithCheck(_ => check);
                        }
                    }
                    return column;
                });
            }
            return columns;
        });
    }

    private static bool ConstraintBoolean(DataField field, string kind) =>
        field.Constraints.FirstOrDefault(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))?.Value is true;

    private static ICheckBuildable? MapCheck(IColumnName column, DataConstraint constraint)
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
}

internal static class ColumnConstraintBuilderExtensions
{
    public static IColumnConstraintBuilder WithPrimaryKeyIf(this IColumnConstraintBuilder builder, bool value)
    { if (value) builder.WithPrimaryKey(); return builder; }
    public static IColumnConstraintBuilder WithUniqueIf(this IColumnConstraintBuilder builder, bool value)
    { if (value) builder.WithUnique(); return builder; }
    public static IColumnConstraintBuilder WithNullableIf(this IColumnConstraintBuilder builder, bool value)
    { if (value) builder.WithNullable(); else builder.WithNotNullable(); return builder; }
}
