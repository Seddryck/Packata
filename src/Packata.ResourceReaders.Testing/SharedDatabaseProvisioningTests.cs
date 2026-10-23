using DubUrl.Mapping;
using DubUrl.Mapping.Implementation;
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
            var prototype = new DuckdbMapper(new DuckDBConnectionStringBuilder(), dialect, new NamedParametrizer(), directory);
            var registry = new Mock<ISchemeRegistry>();
            registry.Setup(x => x.GetMapper("duckdb")).Returns(prototype);
            registry.Setup(x => x.GetProviderFactory(It.IsAny<string[]>())).Returns(DuckDBClientFactory.Instance);
            var connections = new DubUrlConnectionService(registry.Object, directory);
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
}
