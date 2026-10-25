using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class DialectExportCommandTests
{
    [TestCase("datapackage.json", CliWorkspace.PackageJson, "customers-endpoint")]
    [TestCase("datapackage.yaml", CliWorkspace.PackageYaml, "customers-endpoint")]
    [TestCase("contract.yaml", CliWorkspace.Odcs, "local")]
    public async Task Export_WritesOnlyStandaloneJsonAndDoesNotReadCsv(string name, string content, string endpoint)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write(name, content);
        var result = await CliWorkspace.Run("dialect", "export", source, "--endpoint", endpoint);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var json = JObject.Parse(result.Output);
        Assert.That((string?)json["$schema"], Is.EqualTo("https://datapackage.org/profiles/2.0/tabledialect.json"));
        Assert.That((string?)json["delimiter"], Is.EqualTo(","));
        Assert.That(json["resources"], Is.Null);
        Assert.That(result.Output.Contains('\u001b'), Is.False);
    }

    [Test]
    public async Task Export_SelectsExactEndpointAndPreservesLineEndings()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", """
            {"name":"sample","resources":[
              {"name":"comma","path":"comma.csv","format":"csv","dialect":{"delimiter":","}},
              {"name":"semicolon","path":"semicolon.csv","format":"csv","dialect":{"delimiter":";","lineTerminator":"\n","headerRows":[2],"commentRows":[1],"commentChar":"#"}}
            ]}
            """);
        var result = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "semicolon-endpoint");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var json = JObject.Parse(result.Output);
        Assert.That((string?)json["delimiter"], Is.EqualTo(";"));
        Assert.That((string?)json["lineTerminator"], Is.EqualTo("\n"));
        Assert.That(json["headerRows"]!.Values<int>(), Is.EqualTo(new[] { 2 }));
        Assert.That((string?)json["commentChar"], Is.EqualTo("#"));
    }

    [TestCase("absent", "not found")]
    [TestCase("customers-endpoint", "ambiguous")]
    public async Task Export_RejectsMissingOrDuplicateEndpoint(string endpoint, string error)
    {
        using var workspace = new CliWorkspace();
        var json = JObject.Parse(CliWorkspace.PackageJson);
        ((JArray)json["resources"]!).Add(json["resources"]![0]!.DeepClone());
        var source = workspace.Write("datapackage.json", json.ToString());
        var result = await CliWorkspace.Run("dialect", "export", source, "--endpoint", endpoint);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain(error));
    }

    [Test]
    public async Task Export_RejectsMissingOptionAndNonCsvEndpoint()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", CliWorkspace.PackageJson.Replace("\"format\":\"csv\"", "\"format\":\"parquet\""));
        var missing = await CliWorkspace.Run("dialect", "export", source);
        var nonCsv = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint");
        Assert.That(missing.ExitCode, Is.EqualTo(2));
        Assert.That(missing.Error, Does.Contain("endpoint"));
        Assert.That(nonCsv.ExitCode, Is.EqualTo(1));
        Assert.That(nonCsv.Output, Is.Empty);
        Assert.That(nonCsv.Error, Does.Contain("CSV003"));
    }

    [Test]
    public async Task Export_FileOutputRequiresExplicitOverwriteAndProtectsSource()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        var output = System.IO.Path.Combine(workspace.Path, "nested", "dialect.json");
        var created = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint", "--output", output);
        Assert.That(created.ExitCode, Is.Zero, created.Error);
        Assert.That(created.Output, Is.Empty);
        Assert.That((string?)JObject.Parse(File.ReadAllText(output))["delimiter"], Is.EqualTo(","));
        File.WriteAllText(output, "existing");
        var refused = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint", "--output", output);
        Assert.That(refused.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(output), Is.EqualTo("existing"));
        var overwritten = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint", "--output", output, "--overwrite");
        Assert.That(overwritten.ExitCode, Is.Zero, overwritten.Error);
        Assert.That(JObject.Parse(File.ReadAllText(output))["delimiter"], Is.Not.Null);
        var protectedSource = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint", "--output", source, "--overwrite");
        Assert.That(protectedSource.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(source), Is.EqualTo(CliWorkspace.PackageJson));
    }

    [Test]
    public async Task Export_HeaderlessDialectUsesDefaultsAndReportsUnsupportedExtensionsOnStderr()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", CliWorkspace.PackageJson.Replace(
            "\"header\":true", "\"header\":false,\"headerRepeat\":false"));
        var result = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "customers-endpoint");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.Contain("CSV005").And.Contain("headerRepeat"));
        var json = JObject.Parse(result.Output);
        Assert.That((bool?)json["header"], Is.False);
        Assert.That(json["headerRows"]!.Count(), Is.Zero);
        Assert.That(json["headerRepeat"], Is.Null);
    }

    [Test]
    public async Task Export_FailureLeavesOutputUntouchedAndHelpListsSelection()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        var output = workspace.Write("dialect.json", "keep");
        var result = await CliWorkspace.Run("dialect", "export", source, "--endpoint", "absent", "--output", output, "--overwrite");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(File.ReadAllText(output), Is.EqualTo("keep"));
        var help = await CliWorkspace.Run("dialect", "export", "--help");
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("--endpoint").And.Contain("--output").And.Contain("--overwrite"));
    }
}
