using DubUrl.Core.ProviderPacks;
using DuckDB.NET.Data;
using Packata.Core.Reading;
using Packata.Database;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ConnectorAcquisition <curated-catalog.json> <provider-store-path>");
    return 1;
}
using var catalogFile = File.OpenRead(args[0]);
var catalog = ProviderPackCatalog.Read(catalogFile);
var directory = Path.Combine(Path.GetTempPath(), "packata-acquisition-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    using (var seed = new DuckDBConnection("Data Source=" + Path.Combine(directory, "demo.duckdb")))
    {
        seed.Open();
        using var command = seed.CreateCommand();
        command.CommandText = "CREATE TABLE Customers (Id INTEGER); INSERT INTO Customers VALUES (42)";
        command.ExecuteNonQuery();
    }
    using var http = new HttpClient();
    var options = new DatabaseConnectionServiceOptions
    {
        RootPath = directory, Catalog = catalog,
        ProviderStore = new ProviderPackStore(args[1]), HttpClient = http,
        DownloadsEnabled = true // Set false for offline use of already active installed packs.
    };
    // Set ConnectorPreferences["duckdb"] when the supplied catalog lists several technologies
    // without declaring a unique "duckdb" installation alias.
    using var connections = new DubUrlConnectionService(options);
    var readers = ResourceReaderFactory.Create(o => o.AddDatabase(connections));
    var odcs = new Packata.OpenDataContract.DataContract
    {
        Id = "demo", Schema = [new SchemaObject { Name = "customers", PhysicalName = "Customers" }],
        Servers = [new DuckDbServer { Server = "local", Type = "duckdb", Database = "demo.duckdb", Schema = "main" }]
    }.ToCanonicalContract().RequireValue();
    var package = new Packata.DataPackage.DataPackage
    {
        Name = "demo", Resources = [new Resource { Name = "customers",
            Connection = new LiteralConnectionUrl("duckdb://./demo.duckdb"),
            Dialect = new TableDatabaseDialect { Table = "Customers", Namespace = "main" } }]
    }.ToCanonicalContract().RequireValue();
    foreach (var contract in new[] { odcs, package })
    {
        var asset = contract.Assets.Single();
        // The first read loads an installed connector or acquires it. The second reuses the snapshot.
        using var reader = await readers.OpenAsync(new DataEndpointReadRequest(contract.Endpoints.Single(),
            asset.Schema, asset.EndpointBindings.Single().AssetPath), cancellation.Token);
        while (reader.Read()) Console.WriteLine($"Customer ID: {reader[0]}");
    }
    return 0;
}
catch (DatabaseConnectionException error)
{
    Console.Error.WriteLine($"{error.Code}: {error.Message}");
    return 1;
}
catch (OperationCanceledException) { return 2; }
finally { Directory.Delete(directory, true); }
