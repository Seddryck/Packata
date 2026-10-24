using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class InspectCommandTests
{
    [TestCase("contract.yaml", CliWorkspace.Odcs)]
    [TestCase("datapackage.json", CliWorkspace.PackageJson)]
    [TestCase("datapackage.yaml", CliWorkspace.PackageYaml)]
    public async Task Inspect_ReturnsCanonicalContractAndCleanJson(string name, string text)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write(name, text);
        var result = await CliWorkspace.Run("contract", "inspect", source, "--output", "json");
        var json = JObject.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That((string?)json["contract"]!["assets"]![0]!["id"], Is.EqualTo("customers"));
            Assert.That(json["contract"]!["endpoints"]!.Count(), Is.EqualTo(1));
            Assert.That(json["diagnostics"], Is.Not.Null);
            Assert.That(result.Output.Contains('\u001b'), Is.False);
        });
    }

    [Test]
    public async Task Inspect_FiltersAssetsAndEndpointsAndRejectsUnknownAsset()
    {
        using var workspace = new CliWorkspace();
        var json = JObject.Parse(CliWorkspace.PackageJson);
        ((JArray)json["resources"]!).Add(JObject.Parse("{\"name\":\"orders\",\"path\":\"orders.csv\"}"));
        var source = workspace.Write("datapackage.json", json.ToString());
        var result = await CliWorkspace.Run("contract", "inspect", source, "--asset", "customers", "--output", "json");
        var missing = await CliWorkspace.Run("contract", "inspect", source, "--asset", "absent");
        var output = JObject.Parse(result.Output)["contract"]!;
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(output["assets"]!.Count(), Is.EqualTo(1));
            Assert.That(output["endpoints"]!.Count(), Is.EqualTo(1));
            Assert.That(missing.ExitCode, Is.Not.Zero);
            Assert.That(missing.Error, Does.Contain("not found"));
        });
    }

    [Test]
    public async Task Inspect_RejectsAmbiguousAssetIdentifier()
    {
        using var workspace = new CliWorkspace();
        var json = JObject.Parse(CliWorkspace.PackageJson);
        ((JArray)json["resources"]!).Add(json["resources"]![0]!.DeepClone());
        var source = workspace.Write("datapackage.json", json.ToString());
        var result = await CliWorkspace.Run("contract", "inspect", source, "--asset", "customers");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("ambiguous"));
    }

    [Test]
    public async Task Inspect_TextShowsSchemaConstraintsBindingsAndMappingWarnings()
    {
        using var workspace = new CliWorkspace();
        var json = JObject.Parse(CliWorkspace.PackageJson);
        json.Remove("name");
        json["resources"]![0]!["schema"] = JObject.Parse("{\"fields\":[{\"name\":\"id\",\"type\":\"integer\",\"constraints\":{\"required\":true,\"minimum\":1}}]}");
        var source = workspace.Write("datapackage.json", json.ToString());
        var result = await CliWorkspace.Run("contract", "inspect", source);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(result.Output, Does.Contain("Assets").And.Contain("Endpoints").And.Contain("fields").And.Contain("constraints").And.Contain("endpointBindings"));
            Assert.That(result.Error, Does.Contain("DP002").And.Contain("Warning"));
            Assert.That(result.Output.Contains('\u001b'), Is.False);
        });
    }

    [Test]
    public void InspectionRedactsConnectionsAndNestedExtensionSecrets()
    {
        var value = new
        {
            location = new ConnectionLocation("postgres", ConnectionUrl: "postgres://bob:SECRET@host/db"),
            extensions = new { password = "SECRET", endpoint = "https://alice:SECRET@example.test?token=SECRET", note = "pwd=SECRET" }
        };
        var json = CommandOutput.RedactedJson(value).ToString();
        Assert.That(json, Does.Not.Contain("SECRET"));
    }
}
