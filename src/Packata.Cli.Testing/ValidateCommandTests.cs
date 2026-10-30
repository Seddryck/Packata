using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class ValidateCommandTests
{
    [TestCase("contract.yaml", CliWorkspace.Odcs)]
    [TestCase("datapackage.json", CliWorkspace.PackageJson)]
    [TestCase("datapackage.yaml", CliWorkspace.PackageYaml)]
    public async Task Validate_SupportsBothStandardsWithoutOpeningData(string name, string text)
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write(name, text);
        var result = await CliWorkspace.Run("contract", "validate", path, "--output-format", "json");
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That((bool?)json["valid"], Is.True);
            Assert.That((int?)json["schemaVersion"], Is.EqualTo(1));
            Assert.That(result.Error, Is.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(text));
        });
    }

    [Test]
    public async Task Validate_ReportsStructuredDiagnosticsForInvalidPackage()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("invalid.json", "{\"resources\":[]}");
        var result = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json");
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That((bool?)json["valid"], Is.False);
            Assert.That(json["diagnostics"]!.Any(d => (string?)d["sourcePath"] == "$/resources"), Is.True);
            Assert.That((string?)json["diagnostics"]![0]!["severity"], Is.EqualTo("Error"));
        });
    }

    [Test]
    public async Task Validate_RejectsMissingOdcsIdentityAndIncorrectKind()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("invalid.yaml", "kind: Other\nschema: []\n");
        var result = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That((string?)JObject.Parse(result.Output)["diagnostics"]![0]!["code"], Is.EqualTo("VAL_VERSION"));
    }

    [Test]
    public async Task Validate_FormatOverrideWorksForExtensionlessDocument()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", CliWorkspace.Odcs);
        var result = await CliWorkspace.Run("contract", "validate", source, "--input-format", "odcs");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("valid"));
        Assert.That(result.Output.Contains('\u001b'), Is.False);
    }

    [TestCase("", false)]
    [TestCase("--schema", false)]
    [TestCase("--semantic", false)]
    [TestCase("--schema|--semantic", false)]
    [TestCase("--semantic|--schema", false)]
    [TestCase("", true)]
    [TestCase("--schema", true)]
    [TestCase("--semantic", true)]
    [TestCase("--schema|--semantic", true)]
    [TestCase("--semantic|--schema", true)]
    public async Task Validate_AdditiveLayerFlagsSelectExactlyTheRequestedChecks(string flags, bool json)
    {
        using var workspace = new CliWorkspace();
        var text = json ? """{"apiVersion":"v3.2.0","kind":"DataContract","id":"c","version":"1.0.0","status":"draft","schema":[]}""" : CliWorkspace.Odcs;
        var source = workspace.Write("contract.yaml", text);
        var args = new List<string> { "contract", "validate", source, "--output-format", "json" };
        args.AddRange(flags.Split('|', StringSplitOptions.RemoveEmptyEntries));
        var result = await CliWorkspace.Run(args.ToArray());
        var layers = (JArray)JObject.Parse(result.Output)["layers"]!;
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        var expected = flags is "--schema" ? new[] { "Schema" } : flags is "--semantic" ? new[] { "Semantic" } : new[] { "Schema", "Semantic" };
        Assert.That(layers.Select(l => (string?)l["layer"]), Is.EqualTo(expected));
        Assert.That(layers.All(l => (bool?)l["executed"] == true && (string?)l["state"] == "Passed"), Is.True);
    }

    [Test]
    public async Task Validate_SchemaFailureSkipsSemanticAndDoesNotLeakValues()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs + "\ncontractCreatedTs: DO-NOT-EMIT\n");
        var result = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json");
        var report = JObject.Parse(result.Output);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That((string?)report["layers"]![0]!["state"], Is.EqualTo("Failed"));
        Assert.That((string?)report["layers"]![1]!["state"], Is.EqualTo("Skipped"));
        Assert.That(result.Output + result.Error, Does.Not.Contain("DO-NOT-EMIT"));
    }

    [Test]
    public async Task Validate_SemanticOnlyPrerequisiteFailureCannotSucceed()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", "schema: broken\napiVersion: v3.2.0\nkind: DataContract\nid: c\nversion: 1.0.0\nstatus: draft");
        var result = await CliWorkspace.Run("contract", "validate", source, "--semantic", "--output-format", "json");
        var report = JObject.Parse(result.Output);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(report["layers"]!.Count(), Is.EqualTo(1));
        Assert.That((string?)report["layers"]![0]!["layer"], Is.EqualTo("Semantic"));
        Assert.That((string?)report["layers"]![0]!["reason"], Does.Contain("prerequisites"));
        Assert.That(report["diagnostics"]!.Any(d => (string?)d["code"] == "SEM_STRUCTURE"), Is.True);
    }

    [Test]
    public async Task Validate_ZipUsesExistingContainer()
    {
        using var workspace = new CliWorkspace();
        var path = System.IO.Path.Combine(workspace.Path, "package.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("nested/datapackage.json").Open());
            writer.Write(CliWorkspace.PackageJson);
        }
        var result = await CliWorkspace.Run("contract", "validate", path, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
    }

    [Test]
    public async Task Validate_HttpDescriptor()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var body = Encoding.UTF8.GetBytes(CliWorkspace.Odcs);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body);
        });
        var result = await CliWorkspace.Run("contract", "validate", $"http://127.0.0.1:{port}/contract.yaml", "--output-format", "json");
        await serve.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
    }

    [Test]
    public async Task Validate_FailuresHaveStableExitCodesWithoutSourceSnippets()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("broken.json", "{\"password\":\"DO-NOT-EMIT\",broken");
        var invalid = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json");
        var missing = await CliWorkspace.Run("contract", "validate", source + ".missing", "--output-format", "json");
        var invocation = await CliWorkspace.Run("contract", "validate", source, "--output-format", "invalid");
        Assert.Multiple(() =>
        {
            Assert.That(invalid.ExitCode, Is.EqualTo(1));
            Assert.That(invalid.Output + invalid.Error, Does.Not.Contain("DO-NOT-EMIT").And.Not.Contain("Exception"));
            Assert.That(missing.ExitCode, Is.EqualTo(3));
            Assert.That(invocation.ExitCode, Is.EqualTo(2));
            Assert.That(invocation.Error, Is.Not.Empty);
        });
    }
}
