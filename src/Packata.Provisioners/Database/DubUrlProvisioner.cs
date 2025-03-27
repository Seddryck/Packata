using System;
using System.Collections.Generic;
using System.Data;
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
            var builder = new DialectBuilder();
            builder.AddAliases<AnsiDialect>(["ansi"]);
            builder.Build();
            return builder.Get<AnsiDialect>();
        }
    }

    public void DeploySchema(DataPackage dataPackage)
    {
        var schema = new SchemaBuilder()
            .WithTables(tables =>
            {
                foreach (var resource in dataPackage.Resources)
                    tables.Add(Map(resource));
                return tables;
            }).Build();

        var script = ScriptRenderer.Render(schema);
        ScriptDeployer.DeploySchema(ConnectionUrl, script);
    }

    public void DeploySchema(Resource resource)
    {
        var schema = new SchemaBuilder().WithTables(
                tables => { tables.Add(Map(resource)); return tables; }
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

    public void Execute(DataPackage dataPackage)
    {
        DeploySchema(dataPackage);
        LoadData(dataPackage);
    }

    protected internal ITableBuilder Map(Resource resource)
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
                        column.WithName(field.Name
                                ?? throw new InvalidOperationException("Field name is required."))
                            .WithType(DbTypeMapper.Map(field.Type, field.Format))
                    );
                }
                return columns;
            });
    }
}
