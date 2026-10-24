using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using Packata.Provisioners.Database;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;

var directory = Path.Combine(Path.GetTempPath(), "packata-example-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    using var connections = new DubUrlConnectionService(directory);
    var readers = ResourceReaderFactory.Create(options => options.AddDatabase(connections));
    var provisioner = new DubUrlProvisionerBuilder(connections, readers)
        .Using("duckdb").Local().WithDatabase("demo.duckdb").Build();
    var source = new DataEndpoint("inline", null, EndpointKind.Inline, null,
        new InlineLocation(new[] { new Dictionary<string, object?> { ["Id"] = 42 } }));
    var asset = new DataAsset("customers", "Customers", "Customers", null, AssetKind.Table,
        new DataSchema([new DataField("Id", "integer")]), [new EndpointBinding(source.Id)]);
    provisioner.DeploySchema(new Packata.Core.Contracts.DataContract(new("demo"), new(), [asset], [source], new()));
    using (var connection = connections.Resolve(new("duckdb", Database: "demo.duckdb")).Open())
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Customers VALUES (42)";
        command.ExecuteNonQuery();
    }

    var odcs = new Packata.OpenDataContract.DataContract
    {
        Id = "demo", Schema = [new SchemaObject { Name = "customers", PhysicalName = "Customers" }],
        Servers = [new DuckDbServer { Server = "local", Type = "duckdb", Database = "demo.duckdb", Schema = "main" }]
    }.ToCanonicalContract().RequireValue();
    var package = new Packata.DataPackage.DataPackage
    {
        Name = "demo", Resources = [new Resource
        {
            Name = "customers", Connection = new LiteralConnectionUrl("duckdb://./demo.duckdb"),
            Dialect = new TableDatabaseDialect { Table = "Customers", Namespace = "main" }
        }]
    }.ToCanonicalContract().RequireValue();
    foreach (var contract in new[] { odcs, package })
    {
        var mappedAsset = contract.Assets.Single();
        using var reader = await readers.OpenAsync(new DataEndpointReadRequest(contract.Endpoints.Single(),
            mappedAsset.Schema, mappedAsset.EndpointBindings.Single().AssetPath));
        while (reader.Read()) Console.WriteLine($"Customer ID: {reader[0]}");
    }
}
finally { Directory.Delete(directory, true); }
