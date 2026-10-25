using System.Data;
using Microsoft.Data.Sqlite;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Spectre.Console;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class PreviewCommandTests
{
    [Test]
    public async Task Preview_CsvServiceReadsCanonicalHeaders()
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        workspace.Write("customers.csv", "id,name\r\n1,Alice\r\n");
        var source = await ContractSource.LoadAsync(path, "auto", CancellationToken.None);
        var contract = source.Map().RequireValue();
        var sample = await PreviewService.ReadAsync(PreviewService.CreateFactory(source.BaseUri), PreviewService.Request(contract, contract.Assets[0]), 20, null, CancellationToken.None);
        Assert.That(sample.Columns, Is.EqualTo(new[] { "id", "name" }));
    }
    [TestCase("contract.yaml", CliWorkspace.Odcs)]
    [TestCase("datapackage.json", CliWorkspace.PackageJson)]
    [TestCase("datapackage.yaml", CliWorkspace.PackageYaml)]
    public async Task Preview_ExactCommandUsesContractRelativeResourceAndPreservesFiles(string name, string contract)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write(name, contract);
        var content = "id,name\r\n" + string.Join("\r\n", Enumerable.Range(1, 25).Select(index => $"{index},Customer {index}")) + "\r\n";
        var data = workspace.Write("customers.csv", content);
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--limit", "20", "--output-format", "json");
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(json["rows"]!.Count(), Is.EqualTo(20));
            Assert.That(json["columns"]!.Values<string>(), Is.EqualTo(new[] { "id", "name" }));
            Assert.That((string?)json["rows"]![0]![1], Is.EqualTo("Customer 1"));
            Assert.That(File.ReadAllText(source), Is.EqualTo(contract));
            Assert.That(File.ReadAllText(data), Is.EqualTo(content));
            Assert.That(result.Output.Contains('\u001b'), Is.False);
        });
        using var exclusive = File.Open(data, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(25)]
    public async Task Preview_DefaultLimitHandlesShortAndEmptyData(int count)
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(CliWorkspace.PackageJson);
        descriptor["resources"]![0]!["schema"] = JObject.Parse("{\"fields\":[{\"name\":\"id\",\"type\":\"integer\"},{\"name\":\"name\",\"type\":\"string\"}]}");
        var source = workspace.Write("datapackage.json", descriptor.ToString());
        workspace.Write("customers.csv", "id,name\r\n" + string.Concat(Enumerable.Range(1, count).Select(index => $"{index},Customer\r\n")));
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        var json = JObject.Parse(result.Output);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(json["rows"]!.Count(), Is.EqualTo(Math.Min(count, 20)));
        Assert.That(json["columns"]!.Count(), Is.EqualTo(2));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("many")]
    [TestCase("2147483648")]
    public async Task Preview_RejectsInvalidLimitsBeforeOpeningResource(string limit)
    {
        var result = await CliWorkspace.Run("data", "preview", "does-not-exist.yaml", "--asset", "customers", "--limit", limit);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.Not.Empty);
        Assert.That(result.Error, Does.Contain("--limit"));
        Assert.That(result.Output, Is.Empty);
    }

    [TestCase("data|preview")]
    [TestCase("data|preview|does-not-exist.yaml")]
    [TestCase("data|preview|does-not-exist.yaml|--asset|customers|--output-format|xml")]
    [TestCase("data|preview|does-not-exist.yaml|--asset|customers|--columns|id,,name")]
    [TestCase("data|preview|does-not-exist.yaml|--asset|customers|--columns|id,id")]
    public async Task Preview_RequiresArgumentsAndValidOptions(string arguments)
    {
        var result = await CliWorkspace.Run(arguments.Split('|'));
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Output + result.Error);
    }

    [Test]
    public async Task Preview_ColumnsFollowRequestedOrderAndCsvEscapesValues()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        workspace.Write("customers.csv", "id,name\r\n1,\"Alice, \"\"A\"\"\r\nSmith\"\r\n");
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--columns", "name,id", "--output-format", "csv");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output.Replace("\r\n", "\n"), Is.EqualTo("name,id\n\"Alice, \"\"A\"\"\nSmith\",1\n"));
        var missing = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--columns", "absent", "--output-format", "json");
        Assert.That(missing.ExitCode, Is.Not.Zero);
        Assert.That(missing.Output, Is.Empty);
        Assert.That(missing.Error, Does.Contain("unknown or ambiguous"));
    }

    [TestCase("my-book.xlsx", "xlsx", "Belgium")]
    [TestCase("iris.parquet", "parquet", "setosa")]
    public async Task Preview_RegistersBinaryReaders(string resource, string format, string expected)
    {
        using var workspace = new CliWorkspace();
        File.Copy(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", resource), System.IO.Path.Combine(workspace.Path, resource));
        var descriptor = JObject.Parse(CliWorkspace.PackageJson);
        descriptor["resources"]![0]!["path"] = resource;
        descriptor["resources"]![0]!["format"] = format;
        descriptor["resources"]![0]!["dialect"] = JObject.Parse("{\"header\":true,\"sheetName\":\"Country\"}");
        var source = workspace.Write("datapackage.json", descriptor.ToString());
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--limit", "2", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(JObject.Parse(result.Output)["rows"]!.Count(), Is.EqualTo(2));
        Assert.That(result.Output, Does.Contain(expected));
    }

    [Test]
    public async Task Preview_BindingAndResourceFailuresAreActionableAndKeepStdoutClean()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("servers:", "servers:\n  - server: other\n    type: local\n    path: other.csv\n    format: csv"));
        var ambiguous = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        var missing = await CliWorkspace.Run("data", "preview", source, "--asset", "missing", "--output-format", "json");
        source = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        var unreadable = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        foreach (var result in new[] { ambiguous, missing, unreadable })
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Is.Empty);
        }
        Assert.That(ambiguous.Error, Does.Contain("endpoint binding"));
        Assert.That(missing.Error, Does.Contain("not found"));
        Assert.That(unreadable.Error, Does.Contain("customers").And.Contain("endpoint").And.Contain("resource access"));
    }

    [Test]
    public async Task Preview_UnsupportedFormatDoesNotExposeSourceCredentials()
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(CliWorkspace.PackageJson);
        descriptor["resources"]![0]!["path"] = "https://bob:DO-NOT-EMIT@example.test/data.unknown?token=DO-NOT-EMIT";
        descriptor["resources"]![0]!["format"] = "unknown";
        var source = workspace.Write("datapackage.json", descriptor.ToString());
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("unknown").And.Not.Contain("DO-NOT-EMIT"));
    }

    [Test]
    public async Task Preview_HelpExposesNestedHierarchyAndExample()
    {
        var root = await CliWorkspace.Run("--help");
        var branch = await CliWorkspace.Run("data", "--help");
        var preview = await CliWorkspace.Run("data", "preview", "--help");
        Assert.That(root.Output, Does.Contain("data"));
        Assert.That(branch.Output, Does.Contain("preview"));
        Assert.That(preview.ExitCode, Is.Zero);
        Assert.That(preview.Output, Does.Contain("packata data preview contract.yaml --asset customers --limit 20").And.Contain("--columns"));
        Assert.That(preview.Output.Contains('\u001b'), Is.False);
    }

    [Test]
    public void Preview_TableHandlesNullWideValuesAndNarrowTerminal()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors });
        console.Profile.Width = 24;
        PreviewPresenter.Write(new(["name", "note"], [[null, new string('x', 200) + "\r\n[red]markup[/]"]]), "table", console);
        var output = writer.ToString();
        Assert.That(output, Does.Contain("(null)"));
        Assert.That(output.Contains('\u001b'), Is.False);
        Assert.That(output.Contains(new string('x', 200), StringComparison.Ordinal), Is.False);
    }

    [Test]
    public async Task Preview_HelpRemainsPlainWhenCiWouldForceAnsi()
    {
        var environment = new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true", ["TERM"] = "xterm-256color", ["NO_COLOR"] = null
        };
        var result = await CliWorkspace.RunWithEnvironment(environment, "data", "preview", "--help");
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.Output.Contains('\u001b'), Is.False);
            Assert.That(result.Output, Does.Contain("packata data preview contract.yaml --asset customers --limit 20"));
        });
    }

    [Test]
    public async Task Preview_DefaultTableCommandAndJsonNulls()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs);
        workspace.Write("customers.csv", "id,name\r\n1,Alice\r\n2,Bob\r\n");
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--limit", "20");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("Alice").And.Contain("2 row(s)"));
        var json = JObject.Parse(CliWorkspace.PackageJson);
        var resource = (JObject)json["resources"]![0]!;
        resource.Remove("path");
        resource["data"] = JArray.Parse("[{\"id\":1,\"name\":null}]");
        resource["schema"] = JObject.Parse("{\"fields\":[{\"name\":\"id\",\"type\":\"integer\"},{\"name\":\"name\",\"type\":\"string\"}]}");
        source = workspace.Write("datapackage.json", json.ToString());
        result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(JObject.Parse(result.Output)["rows"]![0]![1]!.Type, Is.EqualTo(JTokenType.Null));
    }

    [Test]
    public void Preview_CancellationBeforeOpenNeverOpensReader()
    {
        var factory = new Mock<IDataEndpointReaderFactory>(MockBehavior.Strict);
        Assert.That(async () => await PreviewService.ReadAsync(factory.Object, Request(), 20, null, new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        factory.VerifyNoOtherCalls();
    }

    [Test]
    public void Preview_HostCancellationHasStableExitCode()
    {
        var code = Program.Run(["data", "preview", "does-not-exist.yaml", "--asset", "customers"], new CancellationToken(true));
        Assert.That(code, Is.EqualTo(130));
    }

    [Test]
    public async Task Preview_LimitDisposesReaderWithoutReadingAnExtraRowAndPreservesNull()
    {
        var reader = Reader();
        reader.Setup(value => value.Read()).Returns(true);
        reader.Setup(value => value.IsDBNull(1)).Returns(true);
        var result = await PreviewService.ReadAsync(Factory(reader.Object), Request(), 2, "name,id", CancellationToken.None);
        Assert.That(result.Rows, Has.Count.EqualTo(2));
        Assert.That(result.Rows[0][0], Is.Null);
        reader.Verify(value => value.Read(), Times.Exactly(2));
        reader.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public void Preview_CancellationAndReadFailureDisposeReader()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelled = Reader();
        cancelled.Setup(value => value.Read()).Callback(cancellation.Cancel).Returns(true);
        Assert.That(async () => await PreviewService.ReadAsync(Factory(cancelled.Object), Request(), 20, null, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        cancelled.Verify(value => value.Dispose(), Times.Once);
        var failed = Reader();
        failed.Setup(value => value.Read()).Throws<InvalidDataException>();
        Assert.That(async () => await PreviewService.ReadAsync(Factory(failed.Object), Request(), 20, null, CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
        failed.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public void Preview_AmbiguousColumnsAndEndpointBindingsAreRejected()
    {
        var reader = Reader();
        reader.Setup(value => value.GetName(It.IsAny<int>())).Returns("duplicate");
        Assert.That(async () => await PreviewService.ReadAsync(Factory(reader.Object), Request(), 20, "duplicate", CancellationToken.None), Throws.TypeOf<ContractOperationException>());
        reader.Verify(value => value.Dispose(), Times.Once);
        var asset = new DataAsset("asset", "asset", null, null, AssetKind.Table, null, [new("missing")]);
        var contract = new DataContract(new("test"), new(), [asset], [], new());
        Assert.That(() => PreviewService.Request(contract, asset), Throws.TypeOf<ContractOperationException>());
        Assert.That(() => PreviewService.Request(contract, asset with { EndpointBindings = [new("one"), new("two")] }), Throws.TypeOf<ContractOperationException>());
    }

    private static Mock<IDataReader> Reader()
    {
        var reader = new Mock<IDataReader>();
        reader.SetupGet(value => value.FieldCount).Returns(2);
        reader.Setup(value => value.GetName(0)).Returns("id");
        reader.Setup(value => value.GetName(1)).Returns("name");
        reader.Setup(value => value.GetValue(0)).Returns(1);
        reader.Setup(value => value.GetValue(1)).Returns("Alice");
        return reader;
    }

    private static IDataEndpointReaderFactory Factory(IDataReader reader)
    {
        var factory = new Mock<IDataEndpointReaderFactory>();
        factory.Setup(value => value.OpenAsync(It.IsAny<DataEndpointReadRequest>(), It.IsAny<CancellationToken>()))
            .Returns((DataEndpointReadRequest _, CancellationToken token) => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(reader); });
        return factory.Object;
    }

    private static DataEndpointReadRequest Request() => new(new("test", null, EndpointKind.Inline, null, new InlineLocation(null)));

    [Test]
    public async Task Preview_DatabaseUsesCanonicalTableBindingAndRelativeDatabasePath()
    {
        using var workspace = new CliWorkspace();
        // Load a published provider as an application dependency, without changing a machine connector store.
        _ = typeof(DubUrl.Provider.Sqlite.Discovery.SqliteMetadataReader).Assembly;
        var path = System.IO.Path.Combine(workspace.Path, "customers.db");
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE actual_customers (id INTEGER, name TEXT); INSERT INTO actual_customers VALUES (1, 'Alice'), (2, NULL), (3, 'Bob');";
            await command.ExecuteNonQueryAsync();
        }
        var endpoint = new DataEndpoint("database", null, EndpointKind.Database, null, new ConnectionLocation("sqlite", Database: "./customers.db"));
        var asset = new DataAsset("customers", "customers", "wrong_name", null, AssetKind.Table, null, [new("database", "actual_customers")]);
        var contract = new DataContract(new("db-contract"), new(), [asset], [endpoint], new());
        var request = PreviewService.Request(contract, asset);
        var sample = await PreviewService.ReadAsync(PreviewService.CreateFactory(new Uri(workspace.Path + System.IO.Path.DirectorySeparatorChar)), request, 2, null, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(sample.Rows, Has.Count.EqualTo(2));
            Assert.That(sample.Rows[0][1], Is.EqualTo("Alice"));
            Assert.That(sample.Rows[1][1], Is.Null);
        });
        SqliteConnection.ClearAllPools();
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}

