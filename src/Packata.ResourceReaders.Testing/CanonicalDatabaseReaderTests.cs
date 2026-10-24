using System.Data;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using DubUrl;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Database.Providers;

namespace Packata.ResourceReaders.Testing;

public class CanonicalDatabaseReaderTests
{
    [TestCase(null, "SELECT * FROM [Customer]")]
    [TestCase("Sales", "SELECT * FROM [Sales].[Customer]")]
    public async Task OpenAsync_builds_database_query(string? schema, string expected)
    {
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.SetupSet(value => value.CommandText = It.IsAny<string>());
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(
            new DatabaseConnectionSession(connection.Object, value => $"[{value}]"));
        var endpoint = Endpoint(schema);

        using var reader = await Factory(databases).OpenAsync(endpoint);

        Assert.That(databases.Location, Is.EqualTo(endpoint.Location));
        command.VerifySet(value => value.CommandText = expected, Times.Once);
        command.Verify(value => value.ExecuteReader(), Times.Once);
    }

    [Test]
    public async Task OpenAsync_builds_qualified_database_query_from_inferred_endpoint_metadata()
    {
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.SetupSet(value => value.CommandText = It.IsAny<string>());
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(
            new DatabaseConnectionSession(connection.Object, value => $"[{value}]"));
        var endpoint = Endpoint("sales") with
        {
            Format = new DataFormat("database", Options: new Dictionary<string, object?>
            {
                ["catalog"] = "warehouse",
                ["databaseQualifier"] = "commerce"
            })
        };

        using var reader = await Factory(databases).OpenAsync(
            new DataEndpointReadRequest(endpoint, AssetPath: "Customer"));

        command.VerifySet(value => value.CommandText =
            "SELECT * FROM [warehouse].[commerce].[sales].[Customer]", Times.Once);
    }

    [Test]
    public async Task Disposing_database_reader_disposes_reader_command_and_connection()
    {
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(new DatabaseConnectionSession(connection.Object, value => value));
        var reader = await Factory(databases).OpenAsync(Endpoint(null));

        reader.Dispose();

        inner.Verify(value => value.Dispose(), Times.Once);
        command.Verify(value => value.Dispose(), Times.Once);
        connection.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task Disposal_releases_connection_even_if_reader_and_command_disposal_fail()
    {
        var inner = new Mock<IDataReader>();
        inner.Setup(x => x.Dispose()).Throws(new InvalidOperationException());
        var command = new Mock<IDbCommand>();
        command.Setup(x => x.ExecuteReader()).Returns(inner.Object);
        command.Setup(x => x.Dispose()).Throws(new InvalidOperationException());
        var connection = new Mock<IDbConnection>();
        connection.Setup(x => x.CreateCommand()).Returns(command.Object);
        var service = new StubDatabaseSessionFactory(new(connection.Object, value => value));
        var reader = await Factory(service).OpenAsync(Endpoint(null));
        Assert.That(() => reader.Dispose(), Throws.InvalidOperationException);
        reader.Dispose();
        connection.Verify(x => x.Dispose(), Times.Once);
        command.Verify(x => x.Dispose(), Times.Once);
    }

    [Test]
    public async Task Asset_binding_takes_precedence_over_table_option_and_identifiers_remain_separate()
    {
        var command = new Mock<IDbCommand>();
        command.Setup(x => x.ExecuteReader()).Returns(Mock.Of<IDataReader>());
        var connection = new Mock<IDbConnection>();
        connection.Setup(x => x.CreateCommand()).Returns(command.Object);
        var service = new StubDatabaseSessionFactory(new(connection.Object, value => "[" + value.Replace("]", "]]") + "]"));
        using var reader = await Factory(service).OpenAsync(new DataEndpointReadRequest(Endpoint("schema.part"), AssetPath: "table.part"));
        command.VerifySet(x => x.CommandText = "SELECT * FROM [schema.part].[table.part]", Times.Once);
    }

    [Test]
    public async Task Injected_availability_service_is_awaited_and_can_select_the_runtime_scheme()
    {
        var command = new Mock<IDbCommand>();
        command.Setup(x => x.ExecuteReader()).Returns(Mock.Of<IDataReader>());
        var connection = new Mock<IDbConnection>();
        connection.Setup(x => x.CreateCommand()).Returns(command.Object);
        var service = new StubDatabaseSessionFactory(new(connection.Object, value => value));
        var availability = new Mock<IConnectorAvailabilityService>();
        availability.Setup(x => x.EnsureAvailableAsync("mssql", It.IsAny<CancellationToken>())).ReturnsAsync("sqlserver");
        var factory = ResourceReaderFactory.Create(o => o.AddDatabase(service, availability.Object));
        using var reader = await factory.OpenAsync(Endpoint(null));
        Assert.That(service.Location!.ConnectionUrl, Is.EqualTo("sqlserver://server/database"));
        availability.VerifyAll();
    }

    [Test]
    public async Task Cancellation_after_a_synchronous_query_disposes_reader_command_and_connection()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.Setup(x => x.ExecuteReader()).Callback(() => cancellation.Cancel()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(x => x.CreateCommand()).Returns(command.Object);
        var service = new StubDatabaseSessionFactory(new(connection.Object, value => value));
        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Factory(service).OpenAsync(Endpoint(null), cancellationToken: cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        inner.Verify(x => x.Dispose(), Times.Once);
        command.Verify(x => x.Dispose(), Times.Once);
        connection.Verify(x => x.Dispose(), Times.Once);
    }

    [Test]
    public void OpenAsync_disposes_connection_when_command_execution_fails()
    {
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Throws<InvalidOperationException>();
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(new DatabaseConnectionSession(connection.Object, value => value));

        Assert.That(async () => await Factory(databases).OpenAsync(Endpoint(null)),
            Throws.TypeOf<DatabaseConnectionException>());
        command.Verify(value => value.Dispose(), Times.Once);
        connection.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task OpenAsync_accepts_decomposed_database_connection()
    {
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(new DatabaseConnectionSession(connection.Object, value => value));
        var endpoint = Endpoint(null) with { Location = new ConnectionLocation("mssql", "server", 1433, "database") };

        using var reader = await Factory(databases).OpenAsync(endpoint);

        Assert.That(databases.Location, Is.EqualTo(endpoint.Location));
    }

    [Test]
    public async Task OpenAsync_reads_an_asset_from_a_mapped_odcs_database_endpoint()
    {
        var document = new Packata.OpenDataContract.DataContract
        {
            Id = "sales",
            Schema =
            [
                new SchemaObject
                {
                    Name = "customers", PhysicalName = "Customer",
                    Properties = [new SchemaProperty { Name = "Id" }]
                }
            ],
            Servers =
            [
                new MsSqlServer
                {
                    Server = "production", Type = "sqlserver", Host = "server", Port = 1433,
                    Database = "sales", Schema = "dbo"
                }
            ]
        };
        var contract = document.ToCanonicalContract().RequireValue();
        var asset = contract.Assets.Single();
        var binding = asset.EndpointBindings.Single();
        var endpoint = contract.Endpoints.Single();
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(
            new DatabaseConnectionSession(connection.Object, value => $"[{value}]"));

        using var reader = await Factory(databases).OpenAsync(
            new DataEndpointReadRequest(endpoint, asset.Schema, binding.AssetPath));

        Assert.Multiple(() =>
        {
            Assert.That(databases.Location, Is.EqualTo(endpoint.Location));
            command.VerifySet(value => value.CommandText = "SELECT * FROM [dbo].[Customer]", Times.Once);
        });
    }

    [TestCase("mssql://server/database", "mssql", null, null, null, "mssql://server/database")]
    [TestCase(null, "sqlserver", "db.example", 1433, "sales", "sqlserver://db.example:1433/sales")]
    [TestCase(null, "duckdb", null, null, "warehouse.duckdb", "duckdb://./warehouse.duckdb")]
    public void BuildConnectionUrl_uses_canonical_connection_fields(string? connectionUrl, string scheme,
        string? host, int? port, string? database, string expected)
    {
        var location = new ConnectionLocation(scheme, host, port, database, ConnectionUrl: connectionUrl);

        Assert.That(DatabaseEndpointResolver.BuildConnectionUrl(location), Is.EqualTo(expected));
    }

    [Test]
    public void BuildConnectionUrl_requires_a_host_or_database()
    {
        Assert.That(() => DatabaseEndpointResolver.BuildConnectionUrl(new ConnectionLocation("mssql")),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OpenAsync_requires_database_table()
    {
        var endpoint = Endpoint(null) with { Format = new DataFormat("database") };
        var connection = new Mock<IDbConnection>();
        var databases = new StubDatabaseSessionFactory(new DatabaseConnectionSession(connection.Object, value => value));

        Assert.That(async () => await Factory(databases).OpenAsync(endpoint),
            Throws.TypeOf<ArgumentException>());
        Assert.That(databases.Location, Is.Null, "Table selection must be validated before opening or acquiring a connector.");
    }

    private static DataEndpoint Endpoint(string? schema) => new(
        "customers", "customers", EndpointKind.Database, null,
        new ConnectionLocation("mssql", Namespace: schema, ConnectionUrl: "mssql://server/database"),
        new DataFormat("database", Options: new Dictionary<string, object?> { ["table"] = "Customer" }));

    private static ResourceReaderFactory Factory(IDatabaseConnectionService databases) =>
        ResourceReaderFactory.Create(options => options.AddDatabase(databases));

    private sealed class StubDatabaseSessionFactory(DatabaseConnectionSession session) : IDatabaseConnectionService
    {
        public ConnectionLocation? Location { get; private set; }
        public ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null) => throw new NotSupportedException();
        public ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            Location = (ConnectionLocation)endpoint.Location;
            return ValueTask.FromResult(session);
        }
    }
}
