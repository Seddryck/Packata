using System.Data;
using System.Data.Common;
using DubUrl.Mapping;
using DubUrl.Provider.Sqlite.Mapping;
using DubUrl.Provider.Sqlite.Sql;
using DubUrl.Parsing;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Parametrizing;
using Microsoft.Data.Sqlite;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.ResourceReaders.Database;

namespace Packata.ResourceReaders.Testing;

public class SharedDatabaseConnectionTests
{
    private static readonly SqliteDialect Dialect = CreateDialect();

    private static SqliteDialect CreateDialect()
    {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<SqliteDialect>(["sqlite"]);
        return builder.Build().Get<SqliteDialect>();
    }

    [Test]
    public void Explicit_url_takes_precedence_and_runtime_credentials_and_options_override_url_values()
    {
        var url = DatabaseEndpointResolver.BuildConnectionUrl(
            new ConnectionLocation("ignored", "ignored", 1, "ignored", ConnectionUrl: "mssql://old:old@server/data?mode=old&keep=yes"),
            new("a:b@c", "p/#%:&+", new Dictionary<string, string> { ["MODE"] = "new & value" }));
        Assert.That(url, Is.EqualTo("mssql://a%3Ab%40c:p%2F%23%25%3A%26%2B@server/data?keep=yes&mode=new%20%26%20value"));
    }

    [Test]
    public void Structured_components_are_encoded_and_decoded_once_at_the_mapper_boundary()
    {
        ConnectionUri? observed = null;
        var registry = new Mock<ISchemeRegistry>();
        registry.Setup(x => x.Resolve(It.IsAny<ConnectionUri>())).Callback<ConnectionUri>(value => observed = value)
            .Returns(new ResolvedConnection("", Dialect, Mock.Of<IConnectivity>(), new NamedParametrizer(), SqliteFactory.Instance));
        var service = new DubUrlConnectionService(registry.Object);
        var url = service.Resolve(new("test", "server", 1234, "data / # %25"),
            new("a:b@c", "p/#%:&+", new Dictionary<string, string> { ["option"] = "a & b+%" }));
        _ = url.Parse();
        Assert.Multiple(() =>
        {
            Assert.That(observed!.Username, Is.EqualTo("a:b@c"));
            Assert.That(observed.Password, Is.EqualTo("p/#%:&+"));
            Assert.That(observed.Segments, Is.EqualTo(new[] { "data / # %25" }));
            Assert.That(observed.Options["option"], Is.EqualTo("a & b+%"));
        });
    }

    [TestCase("test://user:secret@server/data#fragment")]
    [TestCase("test://user:secret@server/%GG")]
    [TestCase("user:secret")]
    public void Invalid_urls_do_not_echo_input(string url)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            DatabaseEndpointResolver.BuildConnectionUrl(new("test", ConnectionUrl: url)));
        Assert.That(error!.ToString(), Does.Not.Contain("secret"));
    }

    [Test]
    public void Missing_runtime_password_and_duplicate_options_have_useful_redacted_errors()
    {
        Assert.That(() => DatabaseEndpointResolver.BuildConnectionUrl(new("test", "server"), new("name")),
            Throws.ArgumentException.With.Message.Contains("both username and password"));
        Assert.That(() => DatabaseEndpointResolver.BuildConnectionUrl(
            new("test", ConnectionUrl: "test://server/db?a=1&A=2"), new()),
            Throws.ArgumentException.With.Message.Contains("duplicate provider options"));
        Assert.That(new DatabaseRuntimeOptions("name", "secret").ToString(), Does.Not.Contain("secret"));
    }

    [Test]
    public async Task Concurrent_resolution_has_fresh_mappers_and_no_options_from_previous_requests()
    {
        var service = SqliteService("");
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            service.Resolve(new("sqlite", Database: $"db{i}.db"), i % 2 == 0
                ? new(Options: new Dictionary<string, string> { ["Mode"] = "ReadOnly" }) : null).Parse())));
        for (var i = 0; i < results.Length; i++)
        {
            var parsed = new SqliteConnectionStringBuilder(results[i]);
            Assert.Multiple(() =>
            {
                Assert.That(parsed.DataSource, Is.EqualTo($"db{i}.db"));
                Assert.That(parsed.Mode, Is.EqualTo(i % 2 == 0 ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate));
            });
        }
    }

    [Test]
    public async Task Independent_connections_and_services_keep_runtime_configuration_separate()
    {
        var first = SqliteService("", _ => new(Options: new Dictionary<string, string> { ["Data Source"] = ":memory:" }));
        var endpoint = Endpoint(new("sqlite", Database: "unused"));
        var session1 = await first.OpenAsync(endpoint);
        var session2 = await first.OpenAsync(endpoint);
        using (session1.Connection)
        using (session2.Connection)
        {
            Assert.That(session1.Connection, Is.Not.SameAs(session2.Connection));
            using var command = session1.Connection.CreateCommand();
            command.CommandText = "CREATE TABLE independent (id INTEGER)";
            command.ExecuteNonQuery();
            using var other = session2.Connection.CreateCommand();
            other.CommandText = "SELECT * FROM independent";
            Assert.That(() => other.ExecuteReader(), Throws.TypeOf<SqliteException>());
        }
        var second = SqliteService("");
        Assert.That(second.Resolve(new("sqlite", Database: "other.db")).Parse(), Does.Contain("other.db"));
    }

    [TestCase(false, "Physical Customer")]
    [TestCase(true, "Physical Customer")]
    [TestCase(false, "Customer\"; DROP TABLE test;--")]
    [TestCase(true, "Customer\"; DROP TABLE test;--")]
    public async Task Real_embedded_query_reads_structured_and_mapped_data_package_endpoints(bool mapped, string table)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "database-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data # %.db");
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
                command.CommandText = $"CREATE TABLE {quoted} (Id INTEGER); INSERT INTO {quoted} VALUES (42)";
                command.ExecuteNonQuery();
            }
            var service = SqliteService(directory, _ => new(Options: new Dictionary<string, string> { ["Pooling"] = "false" }));
            DataEndpointReadRequest request;
            if (mapped)
            {
                var package = new Packata.DataPackage.DataPackage
                {
                    Name = "demo", Resources = [new Resource
                    {
                        Name = "customers", Connection = new LiteralConnectionUrl("sqlite://./data%20%23%20%25.db"),
                        Dialect = new TableDatabaseDialect { Table = table, Namespace = "main" }
                    }]
                };
                var contract = package.ToCanonicalContract().RequireValue();
                var asset = contract.Assets.Single();
                request = new(contract.Endpoints.Single(), asset.Schema, asset.EndpointBindings.Single().AssetPath);
            }
            else
            {
                request = new(Endpoint(new("sqlite", Database: "data # %.db", Namespace: "main")), AssetPath: table);
            }

            var factory = ResourceReaderFactory.Create(options => options.AddDatabase(service));
            using var reader = await factory.OpenAsync(request);
            Assert.Multiple(() =>
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(42));
            });
            reader.Close();
            Assert.That(reader.IsClosed, Is.True);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task Open_failure_disposes_connection_and_redacts_provider_exception()
    {
        var connection = new Mock<DbConnection>();
        connection.Setup(x => x.OpenAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("password=secret"));
        var service = FailingService(connection.Object);
        var error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await service.OpenAsync(Endpoint(new("test", "server"))));
        Assert.That(error!.ToString(), Does.Not.Contain("secret"));
        connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
    }

    [Test]
    public void Connection_string_assignment_failure_also_disposes_the_created_connection()
    {
        var connection = new Mock<DbConnection>();
        connection.SetupSet(x => x.ConnectionString = It.IsAny<string>()).Throws(new InvalidOperationException("secret"));
        var service = FailingService(connection.Object);
        Assert.That(async () => await service.OpenAsync(Endpoint(new("test", "server"))), Throws.TypeOf<DatabaseConnectionException>());
        connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
    }

    [Test]
    public async Task Cancellation_is_propagated_without_creating_a_connection()
    {
        var connection = new Mock<DbConnection>();
        var service = FailingService(connection.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.OpenAsync(Endpoint(new("test", "server")), cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        connection.VerifySet(x => x.ConnectionString = It.IsAny<string>(), Times.Never);
    }

    [Test]
    public async Task Cancellation_during_open_disposes_the_connection_and_preserves_the_token()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new Mock<DbConnection>();
        connection.Setup(x => x.OpenAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel()).Returns(Task.CompletedTask);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await FailingService(connection.Object).OpenAsync(Endpoint(new("test", "server")), cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
    }

    [Test]
    public async Task File_readers_do_not_invoke_the_shared_database_service()
    {
        var service = new Mock<IDatabaseConnectionService>(MockBehavior.Strict);
        var availability = new Mock<IConnectorAvailabilityService>(MockBehavior.Strict);
        var factory = ResourceReaderFactory.Create(options => options.AddDatabase(service.Object, availability.Object));
        using var reader = await factory.OpenAsync(new DataEndpoint("inline", null, EndpointKind.Inline, null,
            new InlineLocation(new[] { new Dictionary<string, object?> { ["Id"] = 1 } })));
        Assert.That(reader.Read(), Is.True);
        service.VerifyNoOtherCalls();
        availability.VerifyNoOtherCalls();
    }

    private static DataEndpoint Endpoint(ConnectionLocation location) => new("database", null, EndpointKind.Database, null, location);

    private static DubUrlConnectionService SqliteService(string root, Func<DataEndpoint, DatabaseRuntimeOptions?>? runtime = null)
    {
        var builder = new ProviderCatalogBuilder().WithRootPath(root);
        builder.AddProvider(new ProviderRegistration("SQLite", ["sqlite", "sq"], "Microsoft.Data.Sqlite",
            () => SqliteFactory.Instance, Dialect, () => new NamedParametrizer(),
            context => new SqliteMapper(context.ConnectionStringBuilder, context.Dialect, context.Parametrizer, context.RootPath)));
        return new(builder.Build(), runtimeConfiguration: runtime);
    }

    private static DubUrlConnectionService FailingService(DbConnection connection)
    {
        var registry = new Mock<ISchemeRegistry>();
        var provider = new Mock<DbProviderFactory>();
        provider.Setup(x => x.CreateConnection()).Returns(connection);
        registry.Setup(x => x.Resolve(It.IsAny<ConnectionUri>())).Returns(new ResolvedConnection("Data Source=test",
            Dialect, Mock.Of<IConnectivity>(), new NamedParametrizer(), provider.Object));
        registry.Setup(x => x.CanHandle("test")).Returns(true);
        return new(registry.Object);
    }
}
