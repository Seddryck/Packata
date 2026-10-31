using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DubUrl.Application;
using DubUrl.Core.ProviderPacks;
using DubUrl.Mapping;
using DubUrl.Provider.DuckDb.Mapping;
using DubUrl.Provider.Sqlite.Mapping;
using DubUrl.Provider.Sqlite.Sql;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Parametrizing;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using Packata.ResourceReaders.Database;

namespace Packata.ResourceReaders.Testing;

public class ConnectorIntegrationTests
{
    private string _root = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "connector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [TestCase("odcs")]
    [TestCase("data-package")]
    public async Task First_read_installs_loads_and_queries_in_process_and_repeat_reads_reuse_the_connector(string format)
    {
        var pack = Pack("duckdb");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        using var service = new DubUrlConnectionService(options);
        Assert.That(service.Inspect("duckdb").Loaded, Is.False, "Application provider dependencies are not scanned by explicit configuration.");
        SeedDuckDb();
        var request = MappedRequest(format);
        Assert.That(handler.Count, Is.Zero, "Mapping alone must never download a connector.");
        var factory = ResourceReaderFactory.Create(o => o.AddDatabase(service));
        for (var read = 0; read < 2; read++)
        {
            using var reader = await factory.OpenAsync(request);
            Assert.Multiple(() =>
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(Convert.ToInt64(reader[0]), Is.EqualTo(42));
            });
        }
        var availability = service.Inspect("duckdb");
        Assert.Multiple(() =>
        {
            Assert.That(handler.Count, Is.EqualTo(1));
            Assert.That(availability.Loaded, Is.True);
            Assert.That(availability.Connectors.Single().Listed, Is.True);
            Assert.That(availability.Connectors.Single().Installed, Is.True);
            Assert.That(availability.Connectors.Single().Active, Is.True);
            Assert.That(availability.Connectors.Single().Unhealthy, Is.False);
        });
    }

    [Test]
    public async Task Concurrent_first_reads_download_once_and_existing_readers_survive_later_registry_publication()
    {
        var duck = Pack("duckdb");
        var sqlite = Pack("sqlite");
        using var handler = new Downloads(duck, sqlite);
        using var http = new HttpClient(handler);
        using var service = new DubUrlConnectionService(Options(http, duck, sqlite));
        SeedDuckDb();
        var factory = ResourceReaderFactory.Create(o => o.AddDatabase(service));
        var request = MappedRequest("odcs");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var reader = await factory.OpenAsync(request);
            Assert.That(reader.Read(), Is.True);
        }));
        Assert.That(handler.Count, Is.EqualTo(1));
        using var existing = await factory.OpenAsync(request);
        await service.EnsureAvailableAsync("sqlite");
        Assert.Multiple(() =>
        {
            Assert.That(existing.Read(), Is.True);
            Assert.That(Convert.ToInt64(existing[0]), Is.EqualTo(42));
            Assert.That(handler.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Active_installed_connectors_load_without_network_even_when_downloads_are_disabled()
    {
        var pack = Pack("duckdb");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        await Install(options.ProviderStore!, pack);
        options.DownloadsEnabled = false;
        using var service = new DubUrlConnectionService(options);
        SeedDuckDb();
        using var reader = await ResourceReaderFactory.Create(o => o.AddDatabase(service)).OpenAsync(MappedRequest("odcs"));
        Assert.Multiple(() =>
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(service.Inspect("duckdb").Loaded, Is.True);
            Assert.That(handler.Count, Is.Zero);
        });
    }

    [Test]
    public async Task Offline_unknown_and_ambiguous_connectors_do_not_access_the_network()
    {
        var first = Pack("duckdb", "duck-a", aliases: []);
        var second = Pack("duckdb", "duck-b", aliases: []);
        using var handler = new Downloads(first, second);
        using var http = new HttpClient(handler);
        var options = Options(http, first, second);
        using (var ambiguous = new DubUrlConnectionService(options))
        {
            Assert.That(ambiguous.Inspect("duckdb").Code, Is.EqualTo("connector-ambiguous"));
            var error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await ambiguous.EnsureAvailableAsync("duckdb"));
            Assert.That(error!.Code, Is.EqualTo("connector-ambiguous"));
            error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await ambiguous.EnsureAvailableAsync("unknown"));
            Assert.That(error!.Code, Is.EqualTo("connector-unknown"));
        }
        options.ConnectorPreferences["duckdb"] = "duck-b";
        options.DownloadsEnabled = false;
        using (var offline = new DubUrlConnectionService(options))
        {
            var error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await offline.EnsureAvailableAsync("duckdb"));
            Assert.That(error!.Code, Is.EqualTo("connector-offline"));
        }
        Assert.That(handler.Count, Is.Zero);
        options.DownloadsEnabled = true;
        using var preferred = new DubUrlConnectionService(options);
        await preferred.EnsureAvailableAsync("duckdb");
        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests.Single().AbsolutePath, Does.Contain("duck-b"));
            Assert.That(preferred.Inspect("duckdb").Connectors.Single(x => x.Installed).Id, Is.EqualTo("duck-b"));
        });
    }

    [Test]
    public async Task Catalog_IDs_and_aliases_resolve_to_the_installed_runtime_scheme()
    {
        var pack = Pack("sqlite", aliases: ["friendly"]);
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        using var service = new DubUrlConnectionService(Options(http, pack));
        var scheme = await service.EnsureAvailableAsync("friendly");
        Assert.Multiple(() =>
        {
            Assert.That(scheme, Is.EqualTo("sq"));
            Assert.That(service.Inspect(pack.Entry.Provider).Loaded, Is.True);
            Assert.That(service.Inspect("friendly").Connectors.Single().Id, Is.EqualTo(pack.Entry.Provider));
            Assert.That(handler.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Host_preference_overrides_the_catalog_default_alias_before_acquisition()
    {
        var first = Pack("sqlite", "default-native");
        var preferred = Pack("sqlite", "preferred-native", aliases: []);
        using var handler = new Downloads(first, preferred);
        using var http = new HttpClient(handler);
        var options = Options(http, first, preferred);
        options.ConnectorPreferences["sqlite"] = preferred.Entry.Provider;
        using var service = new DubUrlConnectionService(options);
        await service.EnsureAvailableAsync("sqlite");
        Assert.That(handler.Requests.Single(), Is.EqualTo(preferred.Entry.DownloadUri));
    }

    [Test]
    public async Task Discovery_preserves_versions_that_are_installed_but_no_longer_listed_or_active()
    {
        var older = Pack("sqlite", version: "1.0.0");
        var current = Pack("sqlite", version: "2.0.0");
        using var handler = new Downloads(current);
        using var http = new HttpClient(handler);
        var options = Options(http, current);
        await Install(options.ProviderStore!, older);
        await Install(options.ProviderStore!, current);
        using var service = new DubUrlConnectionService(options);
        var facts = service.Inspect("sqlite").Connectors;
        var old = facts.Single(x => x.Version == "1.0.0");
        var active = facts.Single(x => x.Version == "2.0.0");
        Assert.Multiple(() =>
        {
            Assert.That(old.Listed, Is.False);
            Assert.That(old.Installed, Is.True);
            Assert.That(old.Active, Is.False);
            Assert.That(old.Unhealthy, Is.False);
            Assert.That(active.Listed && active.Installed && active.Active, Is.True);
            Assert.That(handler.Count, Is.Zero);
        });
    }

    [Test]
    public async Task Cancellation_during_download_preserves_the_token_and_store_consistency()
    {
        var pack = Pack("sqlite");
        using var handler = new Downloads(pack) { Block = true };
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        using var service = new DubUrlConnectionService(options);
        using var cancellation = new CancellationTokenSource();
        var opening = service.OpenAsync(new("private", null, EndpointKind.Database, null,
            new ConnectionLocation("sqlite", Database: "query.db")), cancellation.Token).AsTask();
        await handler.Started.Task;
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<ConnectorOperationCanceledException>(async () => await opening);
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(options.ProviderStore!.Inspect(), Is.Empty);
        });
    }

    [Test]
    public async Task Host_injected_application_service_receives_canonical_ID_and_cancellation_token()
    {
        var pack = Pack("sqlite");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        var application = new TrackingConnectorService(new ConnectorService(options.ProviderStore!, options.Catalog!, http));
        options.ConnectorService = application;
        using var cancellation = new CancellationTokenSource();
        using var service = new DubUrlConnectionService(options);
        await service.EnsureAvailableAsync("sqlite", cancellation.Token);
        await service.EnsureAvailableAsync("sqlite", cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(application.Connector, Is.EqualTo(pack.Entry.Provider));
            Assert.That(application.Token, Is.EqualTo(cancellation.Token));
            Assert.That(application.Installations, Is.EqualTo(1));
        });
    }

    [TestCase("integrity", "pack-integrity")]
    [TestCase("api", "api-incompatible")]
    [TestCase("tfm", "tfm-incompatible")]
    [TestCase("rid", "rid-incompatible")]
    [TestCase("load", "connector-load-failed")]
    [TestCase("download", "connector-download-failed")]
    public async Task Acquisition_failures_are_actionable_and_redacted(string failure, string expected)
    {
        var pack = Pack("sqlite", incompatible: failure, badAsset: failure == "load");
        if (failure == "integrity") pack = pack with { Entry = pack.Entry with { Sha256 = new string('0', 64) } };
        using var handler = new Downloads(pack) { Fail = failure == "download" };
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        using var service = new DubUrlConnectionService(options);
        var error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await service.EnsureAvailableAsync("sqlite"));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(expected));
            Assert.That(error.ToString(), Does.Not.Contain("secret"));
            Assert.That(error.Message, Does.Contain(pack.Entry.Provider));
            Assert.That(handler.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Query_failures_do_not_reinstall_or_leak_connection_material()
    {
        var pack = Pack("duckdb");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        using var service = new DubUrlConnectionService(Options(http, pack));
        SeedDuckDb();
        var factory = ResourceReaderFactory.Create(o => o.AddDatabase(service));
        var request = MappedRequest("odcs") with { AssetPath = "missing-secret" };
        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<DatabaseConnectionException>(async () => await factory.OpenAsync(request));
            Assert.Multiple(() =>
            {
                Assert.That(error!.Code, Is.EqualTo("database-query-failed"));
                Assert.That(error.ToString(), Does.Not.Contain("missing-secret"));
            });
        }
        Assert.That(handler.Count, Is.EqualTo(1));
    }

    [TestCase("inactive", "connector-inactive")]
    [TestCase("api", "connector-unhealthy")]
    [TestCase("missing", "connector-unhealthy")]
    [TestCase("corrupt", "connector-unhealthy")]
    [TestCase("unloadable", "connector-unhealthy")]
    [TestCase("driver", "connector-unhealthy")]
    public async Task Store_diagnostics_preserve_independent_facts_and_do_not_hide_an_unrelated_valid_pack(string state, string code)
    {
        var bad = Pack("sqlite");
        var valid = Pack("duckdb");
        using var handler = new Downloads(bad, valid);
        using var http = new HttpClient(handler);
        var options = Options(http, bad, valid);
        var installed = await Install(options.ProviderStore!, bad);
        await Install(options.ProviderStore!, valid);
        var manifest = Path.Combine(installed.InstallPath, ConnectorDefinition.FileName);
        switch (state)
        {
            case "inactive": File.Delete(Path.Combine(options.ProviderStore!.RootPath, "active", bad.Entry.Provider + ".json")); break;
            case "api": File.WriteAllText(manifest, JsonSerializer.Serialize(bad.Entry with { DubUrlApiRange = "[99.0,100.0)" }, Json)); break;
            case "missing": File.Delete(Path.Combine(installed.InstallPath, bad.Entry.Assembly)); break;
            case "corrupt": File.WriteAllText(manifest, "invalid secret JSON"); break;
            case "unloadable": File.WriteAllText(Path.Combine(installed.InstallPath, bad.Entry.Assembly), "invalid secret assembly"); break;
            case "driver": File.WriteAllText(Path.Combine(installed.InstallPath, bad.Entry.DriverAssembly!), "invalid secret driver"); break;
        }
        using var service = new DubUrlConnectionService(options);
        var status = service.Inspect("sqlite");
        Assert.Multiple(() =>
        {
            Assert.That(status.Code, Is.EqualTo(code));
            Assert.That(status.Loaded, Is.False);
            Assert.That(status.Connectors.Any(x => x.Installed), Is.True);
            Assert.That(service.Inspect("duckdb").Loaded, Is.True);
            Assert.That(string.Join(' ', status.Diagnostics.Select(x => x.Message)), Does.Not.Contain("secret"));
            Assert.That(handler.Count, Is.Zero);
        });
    }

    [Test]
    public async Task Explicit_and_installed_alias_collisions_name_both_registrations()
    {
        var pack = Pack("sqlite");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        await Install(options.ProviderStore!, pack);
        options.Modules.Add(new SqliteModule("Explicit SQLite"));
        var error = Assert.Throws<ProviderRegistrationException>(() => new DubUrlConnectionService(options));
        Assert.Multiple(() =>
        {
            Assert.That(error!.ExistingRegistration, Is.EqualTo("Explicit SQLite"));
            Assert.That(error.ConflictingRegistration, Is.EqualTo("SQLite3"));
        });
    }

    [Test]
    public async Task A_collision_after_acquisition_keeps_the_previous_registry_and_names_both_registrations()
    {
        var pack = Pack("duckdb");
        using var handler = new Downloads(pack);
        using var http = new HttpClient(handler);
        var options = Options(http, pack);
        options.Modules.Add(new SqliteModule("Reserved alias", ["duck"]));
        using var service = new DubUrlConnectionService(options);
        var factory = ResourceReaderFactory.Create(o => o.AddDatabase(service));
        var error = await Assert.ThrowsAsync<ProviderRegistrationException>(async () => await factory.OpenAsync(MappedRequest("odcs")));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("Reserved alias").And.Contain("DuckDB"));
            Assert.That(service.Inspect("duck").Loaded, Is.True, "Failed publication must retain the previous registry.");
            Assert.That(handler.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Explicit_modules_are_isolated_and_duplicate_aliases_fail_deterministically()
    {
        var before = DbProviderFactories.GetProviderInvariantNames().Order().ToArray();
        var first = new DatabaseConnectionServiceOptions { RootPath = Path.Combine(_root, "first") };
        first.Modules.Add(new SqliteModule("First"));
        var second = new DatabaseConnectionServiceOptions { RootPath = Path.Combine(_root, "second") };
        second.Modules.Add(new SqliteModule("Second"));
        using var a = new DubUrlConnectionService(first);
        using var b = new DubUrlConnectionService(second);
        Assert.Multiple(() =>
        {
            Assert.That(a.Resolve(new("sqlite", Database: "data.db")).Parse(), Does.Contain("first"));
            Assert.That(b.Resolve(new("sqlite", Database: "data.db")).Parse(), Does.Contain("second"));
            Assert.That(a.Inspect("duckdb").Loaded, Is.False);
            Assert.That(DbProviderFactories.GetProviderInvariantNames().Order(), Is.EqualTo(before));
        });
        first.Modules.Add(new SqliteModule("Conflicting"));
        var error = Assert.Throws<ProviderRegistrationException>(() => new DubUrlConnectionService(first));
        Assert.That(error!.Message, Does.Contain("First").And.Contain("Conflicting"));
    }

    [Test]
    public void Default_store_is_opt_in_and_discovery_rejects_connection_URLs_without_echoing_credentials()
    {
        using var ordinary = new DubUrlConnectionService(new DatabaseConnectionServiceOptions());
        Assert.That(ordinary.ProviderStorePath, Is.Null);
        var previousStore = Environment.GetEnvironmentVariable("DUBURL_PROVIDER_STORE");
        try
        {
            Environment.SetEnvironmentVariable("DUBURL_PROVIDER_STORE", Path.Combine(_root, "default-store"));
            using var optedIn = new DubUrlConnectionService(new DatabaseConnectionServiceOptions { UseDefaultProviderStore = true });
            Assert.That(optedIn.ProviderStorePath, Is.EqualTo(new ProviderPackStore().RootPath));
        }
        finally { Environment.SetEnvironmentVariable("DUBURL_PROVIDER_STORE", previousStore); }
        var error = Assert.Throws<DatabaseConnectionException>(() => ordinary.Inspect("sqlite://user:secret@host/db"));
        Assert.That(error!.ToString(), Does.Not.Contain("secret"));
    }

    private DatabaseConnectionServiceOptions Options(HttpClient http, params TestPack[] packs) => new()
    {
        RootPath = _root, ProviderStore = new ProviderPackStore(Path.Combine(_root, "store")),
        Catalog = new ProviderPackCatalog(packs.Select(p => p.Entry)), HttpClient = http,
        RuntimeConfiguration = endpoint => endpoint.Location is ConnectionLocation { Scheme: "sqlite" }
            ? new(Options: new Dictionary<string, string> { ["Pooling"] = "false" }) : null
    };

    private static Task<InstalledProviderPack> Install(ProviderPackStore store, TestPack pack) =>
        store.InstallAsync(pack.Entry, new MemoryStream(pack.Bytes));

    private DataEndpointReadRequest MappedRequest(string format)
    {
        var contract = format == "odcs"
            ? new Packata.OpenDataContract.DataContract
            {
                Id = "example", Schema = [new SchemaObject { Name = "customers", PhysicalName = "Customers" }],
                Servers = [new DuckDbServer { Server = "local", Type = "duckdb", Database = "query.duckdb", Schema = "main" }]
            }.ToCanonicalContract().RequireValue()
            : new Packata.DataPackage.DataPackage
            {
                Name = "example", Resources = [new Resource { Name = "customers",
                    Connection = new LiteralConnectionUrl("duckdb://./query.duckdb"),
                    Dialect = new TableDatabaseDialect { Table = "Customers", Namespace = "main" } }]
            }.ToCanonicalContract().RequireValue();
        var asset = contract.Assets.Single();
        return new(contract.Endpoints.Single(), asset.Schema, asset.EndpointBindings.Single().AssetPath);
    }

    private void SeedDuckDb()
    {
        using var connection = new DuckDBConnection("Data Source=" + Path.Combine(_root, "query.duckdb"));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Customers (Id INTEGER); INSERT INTO Customers VALUES (42)";
        command.ExecuteNonQuery();
    }

    private static TestPack Pack(string scheme, string? id = null, string[]? aliases = null, string? incompatible = null, bool badAsset = false,
        string version = "1.0.0")
    {
        var sqlite = scheme == "sqlite";
        var implementation = sqlite ? typeof(SqliteMapper).Assembly : typeof(DuckdbMapper).Assembly;
        var driver = sqlite ? typeof(SqliteFactory).Assembly : typeof(DuckDBClientFactory).Assembly;
        var assemblyPath = "lib/" + Path.GetFileName(implementation.Location);
        var driverPath = "lib/" + Path.GetFileName(driver.Location);
        var entry = new ConnectorDefinition
        {
            Provider = id ?? scheme + "-native", Version = version, Aliases = aliases ?? [scheme],
            DubUrlApiRange = incompatible == "api" ? "[99.0,100.0)" : "[0.55,0.56)",
            Assembly = assemblyPath, ImplementationAssemblies = [assemblyPath],
            Driver = driver.GetName().Name!, DriverAssembly = driverPath,
            Databases = [new(sqlite ? "SQLite3" : "DuckDB", sqlite ? ["sq", "sqlite"] : ["duck", "duckdb"])],
            TargetFrameworks = incompatible == "tfm" ? ["net99.0"] : ["net8.0", "net9.0", "net10.0"],
            RuntimeIdentifiers = incompatible == "rid" ? ["unsupported-rid"] : []
        };
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            using (var stream = zip.CreateEntry(ConnectorDefinition.FileName).Open()) JsonSerializer.Serialize(stream, entry, Json);
            using (var stream = zip.CreateEntry(assemblyPath).Open())
                stream.Write(badAsset ? Encoding.UTF8.GetBytes("invalid assembly") : File.ReadAllBytes(implementation.Location));
            using (var stream = zip.CreateEntry(driverPath).Open()) stream.Write(File.ReadAllBytes(driver.Location));
        }
        var bytes = archive.ToArray();
        return new(entry with { DownloadUri = new Uri($"https://catalog.test/{entry.Provider}.zip"),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }, bytes);
    }

    private sealed record TestPack(ConnectorDefinition Entry, byte[] Bytes);

    private sealed class TrackingConnectorService(IConnectorService inner) : IConnectorService
    {
        public int Installations { get; private set; }
        public string? Connector { get; private set; }
        public CancellationToken Token { get; private set; }
        public IReadOnlyList<ConnectorInfo> List() => inner.List();
        public Task<ConnectorInfo> InstallOfflineAsync(string connector, string archivePath, CancellationToken cancellationToken = default)
            => inner.InstallOfflineAsync(connector, archivePath, cancellationToken);
        public Task<ConnectorInfo> InstallAsync(string connector, CancellationToken cancellationToken = default)
        {
            Installations++;
            Connector = connector;
            Token = cancellationToken;
            return inner.InstallAsync(connector, cancellationToken);
        }
        public string Uninstall(string connector) => throw new NotSupportedException();
    }

    private sealed class Downloads(params TestPack[] packs) : HttpMessageHandler
    {
        private int _count;
        public int Count => _count;
        public List<Uri> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block { get; init; }
        public bool Fail { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            Requests.Add(request.RequestUri!);
            Started.TrySetResult();
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Fail) throw new HttpRequestException("https://user:secret@catalog.test/private");
            var pack = packs.Single(p => p.Entry.DownloadUri == request.RequestUri);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(pack.Bytes) };
        }
    }

    private sealed class SqliteModule(string name, string[]? aliases = null) : IDubUrlProviderModule
    {
        public void Register(IProviderCatalogBuilder catalog)
        {
            var dialects = new DialectRegistryBuilder();
            dialects.AddDialect<SqliteDialect>(aliases ?? ["sqlite", "sq"]);
            catalog.AddProvider(new ProviderRegistration(name, aliases ?? ["sqlite", "sq"], "Microsoft.Data.Sqlite",
                () => SqliteFactory.Instance, dialects.Build().Get<SqliteDialect>(), () => new NamedParametrizer(),
                context => new SqliteMapper(context.ConnectionStringBuilder, context.Dialect, context.Parametrizer, context.RootPath)));
        }
    }
}
