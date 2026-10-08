using System.Data;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;

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
            new DatabaseSession(connection.Object, value => $"[{value}]"));
        var endpoint = Endpoint(schema);

        using var reader = await new ResourceReaderFactory(null, databases).OpenAsync(endpoint);

        Assert.That(databases.Location, Is.EqualTo(endpoint.Location));
        command.VerifySet(value => value.CommandText = expected, Times.Once);
        command.Verify(value => value.ExecuteReader(), Times.Once);
    }

    [Test]
    public async Task Disposing_database_reader_disposes_reader_command_and_connection()
    {
        var inner = new Mock<IDataReader>();
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Returns(inner.Object);
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(new DatabaseSession(connection.Object, value => value));
        var reader = await new ResourceReaderFactory(null, databases).OpenAsync(Endpoint(null));

        reader.Dispose();

        inner.Verify(value => value.Dispose(), Times.Once);
        command.Verify(value => value.Dispose(), Times.Once);
        connection.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public void OpenAsync_disposes_connection_when_command_execution_fails()
    {
        var command = new Mock<IDbCommand>();
        command.Setup(value => value.ExecuteReader()).Throws<InvalidOperationException>();
        var connection = new Mock<IDbConnection>();
        connection.Setup(value => value.CreateCommand()).Returns(command.Object);
        var databases = new StubDatabaseSessionFactory(new DatabaseSession(connection.Object, value => value));

        Assert.That(async () => await new ResourceReaderFactory(null, databases).OpenAsync(Endpoint(null)),
            Throws.TypeOf<InvalidOperationException>());
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
        var databases = new StubDatabaseSessionFactory(new DatabaseSession(connection.Object, value => value));
        var endpoint = Endpoint(null) with { Location = new ConnectionLocation("mssql", "server", 1433, "database") };

        using var reader = await new ResourceReaderFactory(null, databases).OpenAsync(endpoint);

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
            new DatabaseSession(connection.Object, value => $"[{value}]"));

        using var reader = await new ResourceReaderFactory(null, databases).OpenAsync(
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

        Assert.That(DubUrlDatabaseSessionFactory.BuildConnectionUrl(location), Is.EqualTo(expected));
    }

    [Test]
    public void BuildConnectionUrl_requires_a_host_or_database()
    {
        Assert.That(() => DubUrlDatabaseSessionFactory.BuildConnectionUrl(new ConnectionLocation("mssql")),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OpenAsync_requires_database_table()
    {
        var endpoint = Endpoint(null) with { Format = new DataFormat("database") };
        var connection = new Mock<IDbConnection>();
        var databases = new StubDatabaseSessionFactory(new DatabaseSession(connection.Object, value => value));

        Assert.That(async () => await new ResourceReaderFactory(null, databases).OpenAsync(endpoint),
            Throws.TypeOf<ArgumentException>());
        connection.Verify(value => value.Dispose(), Times.Once);
    }

    private static DataEndpoint Endpoint(string? schema) => new(
        "customers", "customers", EndpointKind.Database, null,
        new ConnectionLocation("mssql", Namespace: schema, ConnectionUrl: "mssql://server/database"),
        new DataFormat("database", Options: new Dictionary<string, object?> { ["table"] = "Customer" }));

    private sealed class StubDatabaseSessionFactory(DatabaseSession session) : IDatabaseSessionFactory
    {
        public ConnectionLocation? Location { get; private set; }
        public DatabaseSession Open(ConnectionLocation location)
        {
            Location = location;
            return session;
        }
    }
}
