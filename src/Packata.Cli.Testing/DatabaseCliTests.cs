using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DubUrl.Core.ProviderPacks;
using DubUrl.Provider.Sqlite.Mapping;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class DatabaseCliTests
{
    private string _root = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private CliConfiguration Configuration => new(Path.Combine(_root, "config.json"), _ => null);

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void Configuration_defaults_persistence_origins_environment_and_unset()
    {
        var configuration = Configuration;
        Assert.That(configuration.Offline, Is.False);
        Assert.That(configuration.Get("connectors.catalog").Value, Is.EqualTo("bundled"));
        configuration.Set("connectors.offline", "true");
        configuration.Set("connectors.store", "relative-store");
        configuration.Set("connectors.preferred.postgresql", "postgresql-npgsql");
        var reloaded = Configuration;
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Offline, Is.True);
            Assert.That(reloaded.Get("connectors.store").Value, Is.EqualTo(Path.GetFullPath("relative-store")));
            Assert.That(reloaded.Get("connectors.offline").Origin, Is.EqualTo("user"));
            Assert.That(reloaded.Preferences()["postgresql"], Is.EqualTo("postgresql-npgsql"));
        });
        var environment = new CliConfiguration(Path.Combine(_root, "config.json"),
            key => key == "DUBURL_PROVIDER_STORE" ? Path.Combine(_root, "environment") : null);
        Assert.That(environment.Get("connectors.store").Origin, Is.EqualTo("environment:DUBURL_PROVIDER_STORE"));
        Assert.That(environment.Get("connectors.store").UserValue, Is.EqualTo(Path.GetFullPath("relative-store")));
        configuration.Unset("connectors.offline");
        Assert.That(Configuration.Offline, Is.False);
        Assert.That(Configuration.Get("connectors.offline").Origin, Is.EqualTo("default"));
        Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
    }

    [TestCase("unknown", "value")]
    [TestCase("connectors.offline", "yes")]
    [TestCase("connectors.store", "https://user:secret@example.test/private")]
    [TestCase("connectors.preferred.postgresql", "https://secret")]
    [TestCase("connectors.preferred.", "sqlite-native")]
    public void Invalid_settings_are_rejected_without_persisting_or_echoing_values(string key, string value)
    {
        var error = Assert.Throws<CliException>(() => Configuration.Set(key, value));
        Assert.That(error!.Message, Does.Not.Contain(value));
        Assert.That(File.Exists(Path.Combine(_root, "config.json")), Is.False);
    }

    [Test]
    public void Config_actions_work_across_processes_and_json_reports_effective_settings()
    {
        Assert.That(Run("config", "set", "connectors.offline", "true").ExitCode, Is.Zero);
        var get = Run("config", "get", "connectors.offline", "--output-format", "json");
        using var json = JsonDocument.Parse(get.Output);
        Assert.That(json.RootElement[0].GetProperty("value").GetBoolean(), Is.True);
        Assert.That(json.RootElement[0].GetProperty("origin").GetString(), Is.EqualTo("user"));
        Assert.That(get.Error, Is.Empty);
        Assert.That(Run("config", "list").Output, Does.Contain("connectors.catalog").And.Contain("connectors.store"));
        Assert.That(Run("config", "unset", "connectors.offline").ExitCode, Is.Zero);
        Assert.That(Run("config", "get", "connectors.offline").Output, Does.Contain("False").And.Contain("default"));
    }

    [TestCase("schema")]
    [TestCase("config")]
    [TestCase("connector")]
    public void Bare_subjects_show_help(string subject)
    {
        var result = Run(subject);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Does.Contain("COMMANDS"));
    }

    [Test]
    public void Connector_discovery_is_local_filterable_and_does_not_create_a_store()
    {
        Configuration.Set("connectors.store", Path.Combine(_root, "store"));
        using var handler = new Downloads([]) { Fail = true };
        using var connectors = new CliConnectors(Configuration, new HttpClient(handler));
        var ducks = connectors.List("duckdb");
        Assert.That(ducks, Is.Not.Empty);
        Assert.That(ducks.All(info => info.Schemes.Contains("duckdb")), Is.True);
        Assert.That(connectors.Show(ducks[0].Id)[0].Loaded, Is.Null);
        Assert.That(handler.Count, Is.Zero);
        Assert.That(Directory.Exists(Path.Combine(_root, "store")), Is.False);
        var result = Run("connector", "list", "--scheme", "duckdb", "--output-format", "json");
        using var document = JsonDocument.Parse(result.Output);
        Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(ducks.Count));
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public void Connector_text_views_and_verbose_json_work_in_a_redirected_process()
    {
        Configuration.Set("connectors.store", Path.Combine(_root, "store"));
        var compact = Run("connector", "list", "--scheme", "duckdb");
        var detailed = Run("connector", "list", "--scheme", "duckdb", "--verbose");
        var shown = Run("connector", "show", "duckdb-native");
        var json = Run("connector", "list", "--scheme", "duckdb", "--output-format", "json");
        var verboseJson = Run("connector", "list", "--scheme", "duckdb", "--verbose", "--output-format", "json");
        var help = Run("connector", "list", "--help");
        Assert.Multiple(() =>
        {
            foreach (var result in new[] { compact, detailed, shown, json, verboseJson, help })
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Error, Is.Empty);
                Assert.That(result.Output, Does.Not.Contain('\u001b'));
            }
            Assert.That(compact.Output, Does.Contain("CONNECTOR").And.Contain("Not installed").And.Not.Contain("Aliases"));
            Assert.That(detailed.Output, Does.Contain("Aliases").And.Contain("API range").And.Contain("Dependencies"));
            Assert.That(shown.Output, Does.Contain("duckdb-native · 0.46.0").And.Contain("Aliases"));
            Assert.That(verboseJson.Output, Is.EqualTo(json.Output));
            Assert.That(help.Output, Does.Contain("--verbose"));
            Assert.That(Directory.Exists(Path.Combine(_root, "store")), Is.False);
        });
        using var document = JsonDocument.Parse(json.Output);
        Assert.That(document.RootElement[0].GetProperty("loaded").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(document.RootElement[0].GetProperty("prerequisites").GetString(), Does.Contain("Current runtime:"));
    }

    [Test]
    public void Bundled_catalog_contains_pinned_distributions_without_loading_or_installing_connectors()
    {
        var catalog = Configuration.Catalog();
        Assert.That(catalog.Entries, Is.Not.Empty);
        Assert.That(catalog.Entries.All(entry => entry.Version == "0.46.0"), Is.True);
        using var connectors = new CliConnectors(Configuration);
        Assert.That(connectors.List().All(entry => entry.DownloadAvailable), Is.True);
        Assert.That(catalog.Entries.All(entry => entry.DownloadUri?.Scheme == "https"
            && entry.Sha256?.Length == 64 && entry.Sha256.All(Uri.IsHexDigit)), Is.True);
    }

    [Test]
    public async Task Discovery_preserves_installed_only_versions_and_exposes_unhealthy_assets_without_mutating_configuration()
    {
        var pack = ConfigurePack();
        using (var connectors = new CliConnectors(Configuration, new HttpClient(new Downloads(pack.Bytes))))
            await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None);
        File.WriteAllText(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(new ProviderPackCatalogDocument(2, []), Json));
        var original = File.ReadAllText(Path.Combine(_root, "config.json"));
        using var discovery = new CliConnectors(Configuration);
        var installed = discovery.Show(pack.Entry.Provider).Single();
        Assert.That(installed.Installed, Is.True);
        Assert.That(installed.Listed, Is.False);
        var packPath = Configuration.Store.Inspect().Single().InstallPath;
        File.Delete(Path.Combine(packPath, pack.Entry.Assembly));
        Assert.That(discovery.Show(pack.Entry.Provider).Single().Unhealthy, Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_root, "config.json")), Is.EqualTo(original));
    }

    [Test]
    public async Task Install_is_idempotent_and_an_offline_command_can_infer_the_installed_database()
    {
        var pack = ConfigurePack(standalone: true);
        using var handler = new Downloads(pack.Bytes);
        var configuration = Configuration;
        using (var connectors = new CliConnectors(configuration, new HttpClient(handler)))
        {
            var installed = await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None);
            Assert.That(installed.Active, Is.True);
            configuration.Set("connectors.offline", "true");
            await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None);
            Assert.That(handler.Count, Is.EqualTo(1));
        }
        Seed();
        var source = WriteContract();
        var result = Run("schema", "infer", source, "--asset", "customers", "--output", Path.Combine(_root, "enriched.yaml"));
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("integer"));
        Assert.That(result.Error, Is.Empty);
        Assert.That(Run("schema", "infer", source, "--asset", "customers").ExitCode, Is.Zero);
        var preview = Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        Assert.That(preview.ExitCode, Is.Zero, preview.Output + preview.Error);
        using var previewJson = JsonDocument.Parse(preview.Output);
        Assert.That(previewJson.RootElement.GetProperty("rows")[0][0].GetInt64(), Is.EqualTo(42));
    }

    [Test]
    [Explicit("Downloads the pinned SQLite connector from the public DubUrl GitHub Release into an isolated store.")]
    public void Published_catalog_acquires_sqlite_on_first_inference_and_reuses_it_for_offline_preview()
    {
        Configuration.Set("connectors.store", Path.Combine(_root, "published-store"));
        Seed();
        var source = WriteContract();
        var inference = Run("schema", "infer", source, "--asset", "customers");
        Assert.That(inference.ExitCode, Is.Zero, inference.Output + inference.Error);
        Assert.That(inference.Error, Does.Contain("Acquiring connector sqlite-microsoft"));
        var installed = Configuration.Store.Inspect().Single();
        Assert.That(installed.Manifest.Version, Is.EqualTo("0.46.0"));
        Configuration.Set("connectors.offline", "true");
        var preview = Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        Assert.That(preview.ExitCode, Is.Zero, preview.Output + preview.Error);
        using var rows = JsonDocument.Parse(preview.Output);
        Assert.That(rows.RootElement.GetProperty("rows")[0][0].GetInt64(), Is.EqualTo(42));
        Assert.That(preview.Error, Is.Empty);
        Assert.That(Run("connector", "install", "sqlite-microsoft").ExitCode, Is.Zero);
    }

    [Test]
    public async Task First_inference_resolution_acquires_once_and_publishes_the_provider_in_process()
    {
        var pack = ConfigurePack();
        using var handler = new Downloads(pack.Bytes);
        Seed();
        var document = ContractDocument.Load(WriteContract());
        using var database = new CliDatabase(Configuration, _root, document.Contract.Endpoints, [], [], new HttpClient(handler));
        var resolved = await database.ResolveAsync(document.Contract.Endpoints.Single(), CancellationToken.None);
        var result = await new Packata.ResourceReaders.Database.DatabaseDataContractInference().InferAsync(resolved);
        Assert.That(result.RequireContract().Assets, Has.Count.EqualTo(1));
        await database.ResolveAsync(document.Contract.Endpoints.Single(), CancellationToken.None);
        var asset = document.Contract.Assets.Single();
        using var reader = await database.CreateReaders().OpenAsync(new DataEndpointReadRequest(
            document.Contract.Endpoints.Single(), asset.Schema, asset.EndpointBindings.Single().AssetPath));
        Assert.That(reader.Read(), Is.True);
        Assert.That(Convert.ToInt64(reader[0]), Is.EqualTo(42));
        Assert.That(handler.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Offline_missing_unknown_and_cancelled_installations_never_download()
    {
        var pack = ConfigurePack();
        Configuration.Set("connectors.offline", "true");
        using var handler = new Downloads(pack.Bytes);
        using var connectors = new CliConnectors(Configuration, new HttpClient(handler));
        Assert.Throws<CliException>(() => connectors.Show("unknown"));
        await Assert.ThrowsAsync<CliException>(async () => await connectors.InstallAsync("unknown", CancellationToken.None));
        var error = await Assert.ThrowsAsync<CliException>(async () => await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("connector-offline"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await connectors.InstallAsync(pack.Entry.Provider, cancellation.Token));
        Assert.That(handler.Count, Is.Zero);
        var result = Run("schema", "infer", WriteContract(), "--asset", "customers");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("connector-offline"));
        var preview = Run("data", "preview", WriteContract(), "--asset", "customers");
        Assert.That(preview.ExitCode, Is.Not.Zero);
        Assert.That(preview.Error, Does.Contain("connector-offline"));
    }

    [Test]
    public async Task Ambiguous_selection_and_invalid_preferences_fail_before_download_and_override_selects_one_connector()
    {
        var pack = ConfigurePack();
        var first = pack.Entry with { Aliases = [] };
        var second = first with { Provider = "sqlite-alternative" };
        File.WriteAllText(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(new ProviderPackCatalogDocument(2, [first, second]), Json));
        var document = ContractDocument.Load(WriteContract());
        using var handler = new Downloads(pack.Bytes);
        using (var database = new CliDatabase(Configuration, _root, document.Contract.Endpoints, [], [], new HttpClient(handler, false)))
        {
            var error = await Assert.ThrowsAsync<Packata.Database.DatabaseConnectionException>(async () =>
                await database.ResolveAsync(document.Contract.Endpoints.Single(), CancellationToken.None));
            Assert.That(error!.Code, Is.EqualTo("connector-ambiguous"));
        }
        Configuration.Set("connectors.preferred.sqlite", "unknown");
        using (var database = new CliDatabase(Configuration, _root, document.Contract.Endpoints, [], [], new HttpClient(handler, false)))
        {
            var error = await Assert.ThrowsAsync<Packata.Database.DatabaseConnectionException>(async () =>
                await database.ResolveAsync(document.Contract.Endpoints.Single(), CancellationToken.None));
            Assert.That(error!.Code, Is.EqualTo("connector-preference-invalid"));
        }
        Assert.That(handler.Count, Is.Zero);
        using var selected = new CliDatabase(Configuration, _root, document.Contract.Endpoints,
            ["sqlite=sqlite-native"], [], new HttpClient(handler, false));
        await selected.ResolveAsync(document.Contract.Endpoints.Single(), CancellationToken.None);
        Assert.That(handler.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Runtime_environment_override_changes_the_connection_without_persisting_it()
    {
        var pack = ConfigurePack(standalone: true);
        using (var connectors = new CliConnectors(Configuration, new HttpClient(new Downloads(pack.Bytes))))
            await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None);
        Configuration.Set("connectors.offline", "true");
        Seed();
        File.Move(Path.Combine(_root, "warehouse.db"), Path.Combine(_root, "runtime.db"));
        var source = WriteContract();
        var document = ContractDocument.Load(source);
        var endpoint = document.Contract.Endpoints.Single().Id;
        var output = Path.Combine(_root, "enriched.json");
        var result = RunWithEnvironment(new() { ["PACKATA_TEST_DATABASE_URL"] = "sqlite://./runtime.db?Pooling=false" },
            "schema", "infer", source, "--asset", "customers", "--connection-env", endpoint + "=PACKATA_TEST_DATABASE_URL", "--output", output);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(File.ReadAllText(output), Does.Contain("warehouse.db").And.Not.Contain("runtime.db"));
        Assert.That(result.Output + result.Error, Does.Not.Contain("runtime.db"));
        Assert.That(File.ReadAllText(Path.Combine(_root, "config.json")), Does.Not.Contain("runtime.db"));
        var preview = RunWithEnvironment(new() { ["PACKATA_TEST_DATABASE_URL"] = "sqlite://./runtime.db?Pooling=false" },
            "data", "preview", source, "--asset", "customers", "--connection-env", endpoint + "=PACKATA_TEST_DATABASE_URL", "--output-format", "json");
        Assert.That(preview.ExitCode, Is.Zero, preview.Output + preview.Error);
        Assert.That(preview.Output + preview.Error, Does.Not.Contain("runtime.db"));
    }

    [Test]
    public void Canonical_and_legacy_inference_commands_have_equivalent_results_and_file_reads_ignore_connector_configuration()
    {
        File.WriteAllText(Path.Combine(_root, "config.json"), "invalid configuration");
        var source = Path.Combine(_root, "csv.json");
        File.WriteAllText(source, """{"name":"sample","resources":[{"name":"customers","path":"customers.csv","format":"csv","dialect":{"header":true}}]}""");
        File.WriteAllText(Path.Combine(_root, "customers.csv"), "id,name\r\n1,Alice\r\n");
        var canonical = Run("schema", "infer", source, "--asset", "customers");
        var legacy = Run("infer", source, "--asset", "customers");
        Assert.That(canonical.ExitCode, Is.Zero, canonical.Output + canonical.Error);
        Assert.That(legacy, Is.EqualTo(canonical));
        var preview = Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        Assert.That(preview.ExitCode, Is.Zero, preview.Output + preview.Error);
    }

    [Test]
    public async Task Integrity_and_download_failures_are_redacted()
    {
        var pack = ConfigurePack();
        using var handler = new Downloads(pack.Bytes) { Fail = true };
        using var connectors = new CliConnectors(Configuration, new HttpClient(handler));
        var error = await Assert.ThrowsAsync<CliException>(async () => await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("connector-download-failed").And.Not.Contain("secret"));
        using var bad = new CliConnectors(Configuration, new HttpClient(new Downloads([1, 2, 3])));
        var integrity = await Assert.ThrowsAsync<CliException>(async () => await bad.InstallAsync(pack.Entry.Provider, CancellationToken.None));
        Assert.That(integrity!.Message, Does.Contain("pack-integrity"));
    }

    [Test]
    public void Runtime_overrides_validate_endpoints_variables_and_duplicates_without_echoing_connection_material()
    {
        var endpoint = ContractDocument.Load(WriteContract()).Contract.Endpoints.Single();
        var key = endpoint.Id;
        var overrides = CliDatabase.ResolveOverrides([endpoint], [key + "=URL"], _ => "sqlite://user:secret@localhost/data.db");
        Assert.That(overrides, Contains.Key(key));
        Assert.Throws<CliException>(() => CliDatabase.ResolveOverrides([endpoint], [key + "=URL"], _ => null));
        Assert.Throws<CliException>(() => CliDatabase.ResolveOverrides([endpoint], ["missing=URL"], _ => "secret"));
        Assert.Throws<CliException>(() => CliDatabase.ResolveOverrides([endpoint], [key + "=URL", key + "=OTHER"], _ => "secret"));
        var error = Assert.Throws<CliException>(() => CliDatabase.ResolveOverrides([endpoint], [key + "=URL"], _ => "secret"));
        Assert.That(error!.Message, Does.Not.Contain("secret"));
    }

    private (ConnectorDefinition Entry, byte[] Bytes) ConfigurePack(bool standalone = false)
    {
        var implementation = typeof(SqliteMapper).Assembly;
        var driver = typeof(SqliteFactory).Assembly;
        var entry = new ConnectorDefinition
        {
            Provider = "sqlite-native", Version = "1.0.0", Aliases = ["sqlite"], DubUrlApiRange = "[0.55,0.56)",
            Assembly = Path.GetFileName(implementation.Location), ImplementationAssemblies = [Path.GetFileName(implementation.Location)],
            Driver = driver.GetName().Name!, DriverAssembly = Path.GetFileName(driver.Location),
            Databases = [new("SQLite3", ["sqlite", "sq"])], TargetFrameworks = ["net8.0", "net9.0", "net10.0"], RuntimeIdentifiers = []
        };
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            using (var stream = zip.CreateEntry(ConnectorDefinition.FileName).Open()) JsonSerializer.Serialize(stream, entry, Json);
            using (var stream = zip.CreateEntry(entry.Assembly).Open()) stream.Write(File.ReadAllBytes(implementation.Location));
            using (var stream = zip.CreateEntry(entry.DriverAssembly).Open()) stream.Write(File.ReadAllBytes(driver.Location));
            // In-process fixtures borrow the test host's already initialized SQLite dependencies.
            // Subprocess fixtures carry the dependency closure, as a published connector does.
            if (standalone)
            {
                foreach (var dependency in Directory.GetFiles(AppContext.BaseDirectory, "SQLitePCLRaw.*.dll"))
                {
                    using var stream = zip.CreateEntry(Path.GetFileName(dependency)).Open();
                    stream.Write(File.ReadAllBytes(dependency));
                }
                var nativeRoot = Path.Combine(AppContext.BaseDirectory, "runtimes");
                foreach (var native in Directory.GetFiles(nativeRoot, "*e_sqlite3*", SearchOption.AllDirectories))
                {
                    using var stream = zip.CreateEntry(Path.GetRelativePath(AppContext.BaseDirectory, native).Replace('\\', '/')).Open();
                    stream.Write(File.ReadAllBytes(native));
                }
                using var graph = zip.CreateEntry(Path.ChangeExtension(entry.Assembly, ".deps.json")).Open();
                graph.Write(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Packata.Cli.Testing.deps.json")));
            }
        }
        var bytes = archive.ToArray();
        entry = entry with { DownloadUri = new("https://catalog.test/sqlite.zip"), Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        var path = Path.Combine(_root, "catalog.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ProviderPackCatalogDocument(2, [entry]), Json));
        Configuration.Set("connectors.catalog", path);
        Configuration.Set("connectors.store", Path.Combine(_root, "store"));
        return (entry, bytes);
    }

    private void Seed()
    {
        using var connection = new SqliteConnection("Data Source=" + Path.Combine(_root, "warehouse.db") + ";Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE customers(id INTEGER NOT NULL PRIMARY KEY); INSERT INTO customers VALUES(42)";
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task Quality_run_dispatches_sql_and_library_against_offline_sqlite_and_blocks_writes()
    {
        var pack = ConfigurePack(standalone: true);
        using var handler = new Downloads(pack.Bytes);
        using (var connectors = new CliConnectors(Configuration, new HttpClient(handler)))
            await connectors.InstallAsync(pack.Entry.Provider, CancellationToken.None);
        Configuration.Set("connectors.offline", "true");
        Seed();
        var source = Path.Combine(_root, "quality.odcs.yaml");
        File.WriteAllText(source, """
            apiVersion: v3.2.0
            kind: DataContract
            id: sql-quality
            version: 1.0.0
            status: active
            schema:
              - name: customers
                physicalName: customers
                quality:
                  - id: sql-count
                    type: sql
                    query: SELECT count(*) FROM {object}
                    mustBe: 1
                  - id: catalog-count
                    type: library
                    metric: rowCount
                    mustBe: 1
                  - id: attempted-write
                    type: sql
                    query: DELETE FROM {object} RETURNING id
                    mustBe: 42
                  - id: attempted-disable-query-only
                    type: sql
                    query: PRAGMA query_only=OFF; DELETE FROM {object} RETURNING id
                    mustBe: 42
            servers:
              - server: db
                type: custom
                endpointUrl: sqlite://./warehouse.db?Pooling=false
                format: database
                database: warehouse.db
                schema: main
            """);
        using var loaded = await ContractSource.LoadAsync(source, "odcs", CancellationToken.None);
        Assert.That(ContractValidation.Validate(loaded).Where(d => d.Severity == MappingSeverity.Error), Is.Empty);
        Assert.That(loaded.Map().IsSuccessful, Is.True);
        var result = Run("quality", "run", source);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Output + result.Error);
        Assert.That(result.Output, Is.Not.Empty, result.Error);
        var document = Newtonsoft.Json.Linq.JObject.Parse(result.Output);
        OorsReport.Validate(document);
        Assert.That(document["results"]!.Select(r => (string?)r["status"]), Is.EqualTo(new[] { "pass", "pass", "error", "error" }), result.Output);
        Assert.That(document["customProperties"]!.Single(p => (string?)p["property"] == "engines")["value"]!.Count(), Is.EqualTo(2));
        using var connection = new SqliteConnection("Data Source=" + Path.Combine(_root, "warehouse.db") + ";Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM customers";
        Assert.That(command.ExecuteScalar(), Is.EqualTo(1L));
    }

    private string WriteContract()
    {
        var source = Path.Combine(_root, "datapackage.json");
        File.WriteAllText(source, """
            {
              "name": "warehouse",
              "resources": [{
                "name": "customers",
                "connection": "sqlite://./warehouse.db?Pooling=false",
                "dialect": { "table": "customers", "namespace": "main" }
              }]
            }
            """);
        return source;
    }

    private (int ExitCode, string Output, string Error) Run(params string[] arguments) => RunWithEnvironment([], arguments);

    private (int ExitCode, string Output, string Error) RunWithEnvironment(Dictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["PACKATA_CONFIG_HOME"] = _root;
        start.Environment.Remove("DUBURL_PROVIDER_STORE");
        foreach (var variable in environment) start.Environment[variable.Key] = variable.Value;
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private sealed class Downloads(byte[] bytes) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public bool Fail { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (Fail) throw new HttpRequestException("https://user:secret@catalog.test/private");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
