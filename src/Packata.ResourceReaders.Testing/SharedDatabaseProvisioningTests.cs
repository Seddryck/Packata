using DubUrl.Mapping;
using DubUrl.Provider.DuckDb.Mapping;
using DubUrl.Provider.DuckDb.Sql;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Parametrizing;
using DuckDB.NET.Data;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using Packata.Provisioners.Database;
using Packata.ResourceReaders.Database;

namespace Packata.ResourceReaders.Testing;

public class SharedDatabaseProvisioningTests
{
    [Test]
    public async Task Shared_service_provisions_DuckDB_and_reads_mapped_ODCS_and_data_package_resources()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "duckdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dialects = new DialectRegistryBuilder();
            dialects.AddDialect<DuckDbDialect>(["duckdb"]);
            var dialect = dialects.Build().Get<DuckDbDialect>();
            var registry = new ProviderCatalogBuilder().WithRootPath(directory);
            registry.AddProvider(new ProviderRegistration("DuckDB", ["duckdb"], "DuckDB.NET.Data",
                () => DuckDBClientFactory.Instance, dialect, () => new NamedParametrizer(),
                context => new DuckdbMapper(context.ConnectionStringBuilder, context.Dialect, context.Parametrizer, context.RootPath))
                { BulkCopyProvider = new TestDuckDbBulkProvider() });
            using var connections = new DubUrlConnectionService(registry.Build());
            var readers = ResourceReaderFactory.Create(options => options.AddDatabase(connections));
            var provisioner = (DubUrlProvisioner)new DubUrlProvisionerBuilder(connections, readers)
                .Using("duckdb").Local().WithDatabase("example.duckdb").Build();
            var source = new DataEndpoint("source", null, EndpointKind.Inline, null,
                new InlineLocation(new[] { new Dictionary<string, object?> { ["Id"] = 42 } }));
            var asset = new DataAsset("customers", "Customers", "Customers", null, AssetKind.Table,
                new DataSchema([new DataField("Id", "integer")]), [new EndpointBinding(source.Id)]);
            var contract = new Packata.Core.Contracts.DataContract(new("example"), new(), [asset], [source], new());
            Assert.That(provisioner.GetOperationSupport(), Is.EqualTo(new DatabaseOperationSupport(true, true, true)));
            Assert.That(await provisioner.ExecuteAsync(contract), Is.Empty);

            var odcs = new Packata.OpenDataContract.DataContract
            {
                Id = "sales", Schema = [new SchemaObject { Name = "customers", PhysicalName = "Customers" }],
                Servers = [new DuckDbServer { Server = "local", Type = "duckdb", Database = "example.duckdb", Schema = "main" }]
            }.ToCanonicalContract().RequireValue();
            var package = new Packata.DataPackage.DataPackage
            {
                Name = "sales", Resources = [new Resource
                {
                    Name = "customers", Connection = new LiteralConnectionUrl("duckdb://./example.duckdb"),
                    Dialect = new TableDatabaseDialect { Table = "Customers", Namespace = "main" }
                }]
            }.ToCanonicalContract().RequireValue();
            foreach (var mapped in new[] { odcs, package })
            {
                var mappedAsset = mapped.Assets.Single();
                using var reader = await readers.OpenAsync(new DataEndpointReadRequest(mapped.Endpoints.Single(),
                    mappedAsset.Schema, mappedAsset.EndpointBindings.Single().AssetPath));
                Assert.Multiple(() =>
                {
                    Assert.That(reader.Read(), Is.True);
                    Assert.That(Convert.ToInt64(reader[0]), Is.EqualTo(42));
                });
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class TestDuckDbBulkProvider : IBulkCopyProvider
    {
        public IBulkCopyEngine Create(DubUrl.ConnectionUrl url, IBulkCopyDependencyResolver dependencies) => new Engine(url);
        private sealed class Engine(DubUrl.ConnectionUrl url) : IBulkCopyEngine
        {
            public void Write(string tableName, System.Data.IDataReader reader)
            {
                using var connection = (DuckDBConnection)url.Open();
                using var appender = connection.CreateAppender(tableName);
                while (reader.Read()) { var row = appender.CreateRow(); row.AppendValue(Convert.ToInt32(reader[0])); row.EndRow(); }
                appender.Close();
            }
            public void Dispose() { }
        }
    }
}
