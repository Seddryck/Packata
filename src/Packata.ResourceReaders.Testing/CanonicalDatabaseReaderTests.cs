using System.Data;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;

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

        Assert.That(databases.ConnectionUrl, Is.EqualTo("mssql://server/database"));
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
    public void OpenAsync_requires_database_connection_url()
    {
        var endpoint = Endpoint(null) with
        {
            Location = new ConnectionLocation("mssql")
        };

        Assert.That(async () => await new ResourceReaderFactory().OpenAsync(endpoint),
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
        public string? ConnectionUrl { get; private set; }
        public DatabaseSession Open(string connectionUrl)
        {
            ConnectionUrl = connectionUrl;
            return session;
        }
    }
}
