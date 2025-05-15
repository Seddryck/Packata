using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Builders;
using Packata.Core;
using Packata.Core.Provisioning;
using DubUrl;
using DubUrl.Schema;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using Packata.ResourceReaders;
using YamlDotNet.Core.Tokens;

namespace Packata.Provisioners.Database;

public class DubUrlProvisioner : IPackageProvisioner
{
    protected SchemaScriptRenderer ScriptRenderer { get; }
    protected SchemaScriptDeployer ScriptDeployer { get; }
    protected DbTypeMapper DbTypeMapper { get; }
    protected BulkCopyEngineFactory BulkCopyEngineFactory { get; }
    protected ResourceReaderFactory ResourceReaderFactory { get; }

    public ConnectionUrl ConnectionUrl { get; }

    protected internal DubUrlProvisioner(ConnectionUrl connectionUrl, SchemaScriptRenderer? scriptRenderer = null, SchemaScriptDeployer? deployer = null, DbTypeMapper? dbTypeMapper = null, BulkCopyEngineFactory? bulkCopyEngineFactory = null, ResourceReaderFactory? resourceReaderFactory = null)
        => (ConnectionUrl, ScriptRenderer, ScriptDeployer, DbTypeMapper, BulkCopyEngineFactory, ResourceReaderFactory)
                = (connectionUrl,
                    scriptRenderer ?? new(connectionUrl.Dialect, SchemaCreationOptions.None),
                    deployer ?? new(),
                    dbTypeMapper ?? new(),
                    bulkCopyEngineFactory ?? new(),
                    resourceReaderFactory ?? new()
        );

    public DubUrlProvisioner(ConnectionUrl connectionUrl)
        : this(connectionUrl, new(GetDialect(connectionUrl)), new(), new())
    { }

    private static IDialect GetDialect(ConnectionUrl ConnectionUrl)
    {
        try
        {
            return ConnectionUrl.Dialect;
        }
        catch
        {
            var builder = new DialectRegistryBuilder();
            builder.AddDialect<AnsiDialect>(["ansi"]);
            var registry = builder.Build();
            return registry.Get<AnsiDialect>();
        }
    }

    public void DeploySchema(DataPackage dataPackage, ProvisionerOptions options)
    {
        var schema = new SchemaBuilder()
            .WithTables(tables =>
            {
                foreach (var resource in dataPackage.Resources)
                    tables.Add(Map(resource, options));
                return tables;
            }).Build();

        var script = ScriptRenderer.Render(schema);
        ScriptDeployer.DeploySchema(ConnectionUrl, script);
    }

    public void DeploySchema(Resource resource, ProvisionerOptions options)
    {
        var schema = new SchemaBuilder().WithTables(
                tables => { tables.Add(Map(resource, options)); return tables; }
            ).Build();
        var script = ScriptRenderer.Render(schema);
        ScriptDeployer.DeploySchema(ConnectionUrl, script);
    }

    public void LoadData(DataPackage dataPackage)
    {
        foreach (var resource in dataPackage.Resources)
            LoadData(resource);
    }

    public void LoadData(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource, nameof(resource));
        using var bulkCopyEngine = BulkCopyEngineFactory.Create(ConnectionUrl);
        var reader = ResourceReaderFactory.Create(resource);
        bulkCopyEngine.Write(
            resource.Name ?? throw new InvalidDataException("Resource name is required."),
            reader.ToDataReader(resource)
        );
    }

    public void Execute(DataPackage dataPackage, ProvisionerOptions options)
    {
        DeploySchema(dataPackage, options);
        LoadData(dataPackage);
    }

    protected internal ITableBuilder Map(Resource resource, ProvisionerOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource, nameof(resource));

        return new TableBuilder().WithName(resource.Name
            ?? throw new InvalidOperationException("Resource name is required."))
            .WithColumns(columns =>
            {
                foreach (var field in resource.Schema?.Fields
                    ?? throw new InvalidOperationException("Schema is required."))
                {
                    columns.Add(column =>
                    {
                        column.WithName(field.Name
                                ?? throw new InvalidOperationException("Field name is required."))
                            .WithType(DbTypeMapper.Map(field.Type, field.Format))
                            .WithPrimaryKeyIf(
                                resource.Schema.PrimaryKey?.Count==1
                                && resource.Schema.PrimaryKey.Contains(field.Name)
                                && options.Constraints.HasFlag(ConstraintsOptions.PrimaryKey))
                            .WithUniqueIf(
                                field.Constraints?.Get<UniqueConstraint>()?.Value ?? false
                                && options.Constraints.HasFlag(ConstraintsOptions.Unique))
                            .WithNullableIf(
                                !(field.Constraints?.Get<RequiredConstraint>()?.Value ?? false)
                                && options.Constraints.HasFlag(ConstraintsOptions.Required))
                            .WithChecksIf(checks =>
                            {
                                foreach (var constraint in field.Constraints?.TypeOf<Core.CheckConstraint>() ?? [])
                                    checks.Add(Map(column, constraint));
                                return checks;
                            }, options.Constraints.HasFlag(ConstraintsOptions.Checks));

                        return column;
                    });
                }
                return columns;
            });
    }

    protected internal static ICheckBuildable Map(IColumnName column, CheckConstraint check)
    {
        ArgumentNullException.ThrowIfNull(check, nameof(check));
        return ((ICheckBuilder) new CheckBuilder(column))
            .WithComparison(left => check switch
            {
                ILength _ => left.WithFunctionCurrentColumn("Length"),
                _ => left.WithCurrentColumn(),
            }, check switch
            {
                IMinimum _ => ">=",
                IMaximum _ => "<=",
                IMaximumExclusive _ => "<",
                IMinimumExclusive _ => ">",
                _ => throw new ArgumentOutOfRangeException(nameof(check)),
            }, right => right.WithValue(check.Value!));
    }
}

static class ColumnConstraintBuilderExtensions
{
    public static IColumnConstraintBuilder WithPrimaryKeyIf(this IColumnConstraintBuilder builder, bool value)
    {
        if (value)
            builder.WithPrimaryKey();
        return builder;
    }

    public static IColumnConstraintBuilder WithUniqueIf(this IColumnConstraintBuilder builder, bool value)
    {
        if (value)
            builder.WithUnique();
        return builder;
    }

    public static IColumnConstraintBuilder WithNullableIf(this IColumnConstraintBuilder builder, bool value)
    {
        if (value)
            builder.WithNullable();
        else
            builder.WithNotNullable();
        return builder;
    }

    public static IColumnConstraintBuilder WithChecks(this IColumnConstraintBuilder builder, Func<IList<ICheckBuildable>, IList<ICheckBuildable>> checks)
    {
        foreach (var check in checks([]))
            builder.WithCheck(_ => check);
        return builder;
    }

    public static IColumnConstraintBuilder WithChecksIf(this IColumnConstraintBuilder builder, Func<IList<ICheckBuildable>, IList<ICheckBuildable>> checks, bool value)
    {
        if(value)
            builder.WithChecks(checks);
        return builder;
    }
}
