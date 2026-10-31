using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class DiffCommandTests
{
    [TestCase(CliWorkspace.Odcs)]
    [TestCase(CliWorkspace.PackageJson)]
    [TestCase(CliWorkspace.PackageYaml)]
    public async Task EqualContracts_HaveExplicitEmptyTextAndVersionedJson(string content)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("previous", content);
        var candidate = workspace.Write("current", content);
        var text = await CliWorkspace.Run("contract", "diff", baseline, candidate);
        var result = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(text.ExitCode, Is.Zero, text.Error);
            Assert.That(text.Output, Does.Contain("Baseline (previous):").And.Contain(baseline)
                .And.Contain("Candidate (current):").And.Contain(candidate).And.Contain("No differences detected."));
            Assert.That((text.Output + result.Output).Contains('\u001b'), Is.False);
            Assert.That(json.Properties().Select(x => x.Name), Is.EqualTo(new[]
                { "schemaVersion", "baseline", "candidate", "hasChanges", "changes", "diagnostics" }));
            Assert.That((int?)json["schemaVersion"], Is.EqualTo(1));
            Assert.That((bool?)json["hasChanges"], Is.False);
            Assert.That(json["changes"], Is.Empty);
        });
    }

    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            properties:
              - name: amount
                logicalType: integer
              - name: old
                logicalType: string
        """;

    [Test]
    public async Task PropertyChanges_AreDirectionalDeterministicAndMatchLibraryOrdering()
    {
        using var workspace = new CliWorkspace();
        // An open unordered schema isolates property changes from closed-set/order changes.
        var openRecord = Odcs.Replace("    properties:", "    customProperties:\n      - vendor: packata\n        property: allowAdditionalProperties\n        value: true\n      - vendor: packata\n        property: enforcePropertyOrder\n        value: false\n    properties:");
        var baseline = workspace.Write("old.yaml", openRecord);
        var candidate = workspace.Write("new.yaml", openRecord.Replace("logicalType: integer", "logicalType: number").Replace("name: old", "name: added"));
        var forward = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output-format", "json");
        var again = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output-format", "json");
        var reverse = await CliWorkspace.Run("contract", "diff", candidate, baseline, "--output-format", "json");
        var text = await CliWorkspace.Run("contract", "diff", baseline, candidate);
        Assert.That(forward.ExitCode, Is.Zero, forward.Error);
        var changes = (JArray)JObject.Parse(forward.Output)["changes"]!;
        var reversed = (JArray)JObject.Parse(reverse.Output)["changes"]!;
        var library = (await ContractSource.LoadAsync(baseline, "auto", default)).Map().RequireValue()
            .Diff((await ContractSource.LoadAsync(candidate, "auto", default)).Map().RequireValue());
        Assert.Multiple(() =>
        {
            Assert.That(again.Output, Is.EqualTo(forward.Output));
            Assert.That(reverse.ExitCode, Is.Zero, reverse.Error);
            Assert.That(changes.Select(x => (string?)x["path"]), Is.EqualTo(library.Changes.Select(x => x.Path.Value)));
            Assert.That(changes.Select(x => (string?)x["kind"]), Is.EqualTo(new[] { "PropertyAdded", "PropertyTypeChanged", "PropertyRemoved" }));
            Assert.That((string?)changes[1]["previous"], Is.EqualTo("integer"));
            Assert.That((string?)changes[1]["current"], Is.EqualTo("number"));
            Assert.That((string?)reversed[1]["previous"], Is.EqualTo("number"));
            Assert.That((string?)reversed[1]["current"], Is.EqualTo("integer"));
            Assert.That((string?)reversed[0]["kind"], Is.EqualTo("PropertyRemoved"));
            Assert.That(text.Output, Does.Contain("modified property schema.orders.properties.amount.type")
                .And.Contain("previous: \"integer\"").And.Contain("current: \"number\""));
            Assert.That(File.ReadAllText(baseline), Is.EqualTo(openRecord));
        });
    }

    [Test]
    public async Task DataPackageChanges_UseIndependentDetectionAndFormatOverrides()
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline.yaml", CliWorkspace.PackageJson);
        var candidate = workspace.Write("candidate.json", CliWorkspace.PackageYaml.Replace("customers.csv", "new.csv"));
        var result = await CliWorkspace.Run("contract", "diff", baseline, candidate,
            "--baseline-format", "datapackage", "--candidate-format", "auto", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var change = JObject.Parse(result.Output)["changes"]!.Single(x => (string?)x["kind"] == "ServerModified");
        Assert.That((string?)change["current"]!["location"]!["paths"]![0], Is.EqualTo("new.csv"));
        var odcs = workspace.Write("odcs", CliWorkspace.Odcs);
        var wrong = await CliWorkspace.Run("contract", "diff", odcs, candidate, "--candidate-format", "odcs", "--output-format", "json");
        Assert.That(wrong.ExitCode, Is.EqualTo(1));
        Assert.That(wrong.Output, Is.Empty);
        Assert.That(wrong.Error, Does.Contain("candidate"));
    }

    [Test]
    public async Task CrossStandardComparisons_UseTheCanonicalApiWithoutCliMatching()
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("odcs", CliWorkspace.Odcs);
        var candidate = workspace.Write("package", CliWorkspace.PackageJson);
        var result = await CliWorkspace.Run("contract", "diff", baseline, candidate,
            "--baseline-format", "odcs", "--candidate-format", "datapackage", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var before = (await ContractSource.LoadAsync(baseline, "auto", default)).Map().RequireValue();
        var after = (await ContractSource.LoadAsync(candidate, "auto", default)).Map().RequireValue();
        Assert.That(JObject.Parse(result.Output)["changes"]!.Select(x => (string?)x["path"]),
            Is.EqualTo(before.Diff(after).Changes.Select(x => x.Path.Value)));
    }

    [TestCase("baseline")]
    [TestCase("candidate")]
    public async Task InvalidInputs_EmitOnlyAttributedErrors(string input)
    {
        using var workspace = new CliWorkspace();
        var valid = workspace.Write("valid", CliWorkspace.Odcs);
        var invalid = workspace.Write("invalid", "kind: Other\nschema: []\n");
        var result = await CliWorkspace.Run("contract", "diff", input == "baseline" ? invalid : valid,
            input == "candidate" ? invalid : valid, "--output-format", "json");
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Does.Contain(input).And.Contain(invalid).And.Contain("Error"));
        });
    }

    [TestCase("broken", "{\"password\":\"DO-NOT-EMIT\",broken", 1)]
    [TestCase("invalid-package", "{\"resources\":[]}", 1)]
    [TestCase("unsupported", "[1,2]", 1)]
    [TestCase("missing", null, 3)]
    public async Task FailedComparison_NeverLooksLikeAnEmptyDiff(string name, string? content, int exitCode)
    {
        using var workspace = new CliWorkspace();
        var valid = workspace.Write("valid", CliWorkspace.Odcs);
        var candidate = content is null ? Path.Combine(workspace.Path, name) : workspace.Write(name, content);
        var result = await CliWorkspace.Run("contract", "diff", valid, candidate, "--output-format", "json", "--quiet");
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Does.Contain("candidate").And.Not.Contain("DO-NOT-EMIT"));
        });
    }

    [Test]
    public async Task LockedSource_UsesIoExitCode()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs);
        using var locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(3));
        Assert.That(result.Output, Is.Empty);
    }

    [TestCase("--baseline-format", "xml")]
    [TestCase("--candidate-format", "xml")]
    [TestCase("--output-format", "csv")]
    public async Task InvalidOptions_UseInvocationExitCode(string option, string value)
    {
        var result = await CliWorkspace.Run("contract", "diff", "a", "b", option, value);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task RequiredArgumentsAndHelp_DocumentDirectionAndOptions()
    {
        var missing = await CliWorkspace.Run("contract", "diff", "one");
        var help = await CliWorkspace.Run("contract", "diff", "--help");
        Assert.That(missing.ExitCode, Is.EqualTo(2));
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("BASELINE").And.Contain("CANDIDATE").And.Contain("Previous")
            .And.Contain("--baseline-format").And.Contain("--candidate-format").And.Contain("--quiet").And.Contain("--output"));
    }

    [TestCase("text")]
    [TestCase("json")]
    public async Task Quiet_SuppressesBothRepresentationsButRetainsWarnings(string output)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("package", CliWorkspace.PackageJson.Replace("\"name\":\"sample\",", ""));
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--output-format", output, "--quiet");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("baseline").And.Contain("candidate").And.Contain("DP002"));
    }

    [Test]
    public async Task JsonWarnings_IdentifyBothInputs()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("package", CliWorkspace.PackageJson.Replace("\"name\":\"sample\",", ""));
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var diagnostics = JObject.Parse(result.Output)["diagnostics"]!;
        Assert.That(diagnostics.Select(x => (string?)x["input"]), Is.EqualTo(new[] { "baseline", "candidate" }));
        Assert.That(diagnostics.All(x => (string?)x["source"] == source), Is.True);
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public async Task LocalAndHttpZipDescriptors_DoNotAccessReferencedDataOrConnectorConfiguration()
    {
        using var workspace = new CliWorkspace();
        var zipPath = Path.Combine(workspace.Path, "package.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("nested/datapackage.json").Open());
            writer.Write(CliWorkspace.PackageJson.Replace("customers.csv", "http://127.0.0.1:1/must-not-open.csv"));
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var bytes = File.ReadAllBytes(zipPath);
        var serve = Serve(listener, bytes, timeout.Token);
        var result = await CliWorkspace.RunWithEnvironment(new Dictionary<string, string?>
        {
            ["PACKATA_CONFIG_HOME"] = workspace.Write("invalid-config-directory", "not a directory"),
            ["DUBURL_PROVIDER_STORE"] = workspace.Write("invalid-provider-directory", "not a directory")
        }, "contract", "diff", zipPath, $"http://127.0.0.1:{port}/package.zip", "--output-format", "json");
        await serve;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["hasChanges"], Is.False);
    }

    [Test]
    public async Task HttpDescriptor_IsLoadedWithoutOpeningDatabaseEndpoints()
    {
        using var workspace = new CliWorkspace();
        var document = Odcs + "\nservers:\n  - server: unavailable\n    type: postgresql\n    host: 127.0.0.1\n    port: 1\n    database: must-not-open\n";
        var baseline = workspace.Write("baseline", document);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serve = Serve(listener, Encoding.UTF8.GetBytes(document), timeout.Token);
        var result = await CliWorkspace.Run("contract", "diff", baseline, $"http://127.0.0.1:{port}/contract.yaml", "--output-format", "json");
        await serve;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["hasChanges"], Is.False);
    }

    [Test]
    public async Task InvalidZipDescriptor_IsAnInvalidInputRatherThanASuccessfulEmptyDiff()
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", CliWorkspace.Odcs);
        var candidate = workspace.Write("invalid.zip", "not a zip archive");
        var result = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("candidate").And.Contain("CLI021"));
    }

    [Test]
    public async Task CancelledComparison_UsesCancellationExitCode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var command = new DiffCommand();
        var exit = await command.ExecuteAsync(null!, new ContractComparisonSettings { Baseline = "missing", Candidate = "missing" }, cancellation.Token);
        Assert.That(exit, Is.EqualTo(130));
    }

    [TestCase("text")]
    [TestCase("json")]
    public async Task NestedCredentialsAndDescriptions_AreRedactedInBothRepresentations(string output)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline", CliWorkspace.Odcs);
        var candidate = workspace.Write("candidate", CliWorkspace.Odcs.Replace("customers.csv", "https://alice:DO-NOT-EMIT@example.test/data?token=DO-NOT-EMIT")
            + "\n    description: 'pwd=DO-NOT-EMIT'\n    customProperties:\n      - property: settings\n        value:\n          password: DO-NOT-EMIT\n          nested:\n            apiKey: DO-NOT-EMIT\n");
        var result = await CliWorkspace.Run("contract", "diff", baseline, candidate, "--output-format", output);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("[redacted]"));
        Assert.That(result.Output + result.Error, Does.Not.Contain("DO-NOT-EMIT"));
        Assert.That(result.Output.Contains('\u001b'), Is.False);
    }

    [Test]
    public async Task FailedInputSourceIdentifiers_AreRedacted()
    {
        using var workspace = new CliWorkspace();
        var source = Path.Combine(workspace.Path, "token=DO-NOT-EMIT");
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(3));
        Assert.That(result.Error, Does.Contain("baseline").And.Contain("[redacted]").And.Not.Contain("DO-NOT-EMIT"));
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task LibraryUnsupportedValues_FailExplicitlyRatherThanReturningEmptyDiff()
    {
        using var workspace = new CliWorkspace();
        var package = JObject.Parse(CliWorkspace.PackageJson);
        package["sources"] = JArray.Parse("[{\"title\":\"Origin\",\"path\":\"https://example.test\"}]");
        var source = workspace.Write("package", package.ToString());
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("comparison").And.Contain("unsupported"));
        Assert.That(result.Output, Is.Empty);
    }

    private static async Task Serve(TcpListener listener, byte[] body, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }
}
