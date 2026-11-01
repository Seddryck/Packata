using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class CliOptionConsistencyTests
{
    [TestCase("contract|validate", "json")]
    [TestCase("contract|validate", "text")]
    [TestCase("contract|inspect", "json")]
    [TestCase("contract|inspect", "text")]
    [TestCase("contract|compare", "json")]
    [TestCase("contract|diff", "json")]
    [TestCase("contract|compare", "text")]
    [TestCase("contract|diff", "text")]
    [TestCase("data|preview", "json")]
    [TestCase("data|preview", "csv")]
    [TestCase("data|preview", "table")]
    public async Task Reports_WriteTheSelectedRepresentationToANewFile(string command, string format)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs);
        workspace.Write("customers.csv", "id,name\r\n1,Alice\r\n");
        var arguments = Arguments(command, source);
        var stdout = await CliWorkspace.Run([.. arguments, "--output-format", format]);
        var output = Path.Combine(workspace.Path, "json");
        var file = await CliWorkspace.Run([.. arguments, "--output-format", format, "--output", output]);

        Assert.Multiple(() =>
        {
            Assert.That(stdout.ExitCode, Is.Zero, stdout.Error);
            Assert.That(file.ExitCode, Is.Zero, file.Error);
            Assert.That(file.Output, Is.Empty);
            Assert.That(File.ReadAllText(output), Is.EqualTo(stdout.Output));
            Assert.That(File.ReadAllText(output), Does.Not.Contain('\u001b'));
            Assert.That(File.ReadAllText(source), Is.EqualTo(CliWorkspace.Odcs));
        });
        if (format == "json") Assert.DoesNotThrow(() => JToken.Parse(File.ReadAllText(output)));
    }

    [TestCase("contract|validate")]
    [TestCase("contract|inspect")]
    [TestCase("contract|compare")]
    [TestCase("contract|diff")]
    [TestCase("data|preview")]
    public async Task Reports_ProtectExistingFilesIncludingTheSource(string command)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs);
        workspace.Write("customers.csv", "id,name\r\n1,Alice\r\n");
        var existing = workspace.Write("json", "keep me");
        var arguments = Arguments(command, source);
        var file = await CliWorkspace.Run([.. arguments, "--output-format", "json", "-o", existing]);
        var input = await CliWorkspace.Run([.. arguments, "--output-format", "json", "-o", source]);

        Assert.Multiple(() =>
        {
            Assert.That(file.ExitCode, Is.EqualTo(3), file.Error);
            Assert.That(input.ExitCode, Is.EqualTo(3), input.Error);
            Assert.That(file.Output + input.Output, Is.Empty);
            Assert.That(File.ReadAllText(existing), Is.EqualTo("keep me"));
            Assert.That(File.ReadAllText(source), Is.EqualTo(CliWorkspace.Odcs));
        });
    }

    [TestCase("contract|validate")]
    [TestCase("contract|inspect")]
    [TestCase("contract|compare")]
    [TestCase("contract|diff")]
    [TestCase("schema|infer")]
    [TestCase("schema|export")]
    [TestCase("data|preview")]
    [TestCase("dialect|export")]
    [TestCase("dialect|infer")]
    public async Task SourceCommands_RejectInvalidInputFormatsBeforeReading(string command)
    {
        var arguments = Arguments(command, "missing");
        var result = await CliWorkspace.Run([.. arguments, "--input-format", "xml"]);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Does.Contain("--input-format"));
        });
    }

    [TestCase("contract|validate")]
    [TestCase("contract|inspect")]
    [TestCase("contract|compare")]
    [TestCase("contract|diff")]
    [TestCase("data|preview")]
    [TestCase("schema|export")]
    [TestCase("dialect|export")]
    public async Task InvalidOutputOptions_DoNotCreateDestinations(string command)
    {
        using var workspace = new CliWorkspace();
        var output = Path.Combine(workspace.Path, "result");
        var result = await CliWorkspace.Run([.. Arguments(command, "missing"), "--output-format", "xml", "-o", output]);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(File.Exists(output), Is.False);
    }

    [TestCase("config|list")]
    [TestCase("connector|list")]
    public async Task DiscoveryCommands_SupportRepresentationAndDestinationIndependently(string command)
    {
        using var workspace = new CliWorkspace();
        var output = Path.Combine(workspace.Path, "result.json");
        var result = await CliWorkspace.RunWithEnvironment(new Dictionary<string, string?>
        {
            ["PACKATA_CONFIG_HOME"] = workspace.Path,
            ["DUBURL_PROVIDER_STORE"] = Path.Combine(workspace.Path, "connectors")
        }, [.. command.Split('|'), "--output-format", "json", "-o", output]);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Is.Empty);
        Assert.That(JToken.Parse(File.ReadAllText(output)), Is.TypeOf<JArray>());
    }

    [TestCase("datapackage", CliWorkspace.PackageYaml)]
    [TestCase("odcs", CliWorkspace.Odcs)]
    public async Task Inference_UsesInputFormatAndResolvesRelativeCsvPaths(string format, string document)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", document);
        workspace.Write("customers.csv", "id,name\n1,Alice\n2,Bob\n");
        var output = Path.Combine(workspace.Path, "enriched.yaml");
        var result = await CliWorkspace.Run("schema", "infer", source, "--input-format", format,
            "--asset", "customers", "--header", "present", "--output", output);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(ContractDocument.Load(output, format).Contract.Assets.Single().Schema!.Fields.Select(field => field.Name),
            Is.EqualTo(new[] { "id", "name" }));
        Assert.That(File.ReadAllText(source), Is.EqualTo(document));
    }

    [Test]
    public async Task SchemaExport_requires_the_canonical_grouped_command()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", CliWorkspace.Odcs.Replace("physicalType: file", "physicalType: file\n    properties:\n      - name: id\n        logicalType: integer"));
        var canonical = await CliWorkspace.Run("schema", "export", source, "--input-format", "odcs", "--asset", "customers", "--output-format", "json-schema");
        var compatibility = await CliWorkspace.Run("export", source, "--input-format", "odcs", "--asset", "customers", "-f", "json-schema");
        Assert.That(canonical.ExitCode, Is.Zero, canonical.Error);
        Assert.That(compatibility.ExitCode, Is.EqualTo(2), compatibility.Error);
        Assert.That(compatibility.Output, Is.Empty);
        Assert.That(compatibility.Error, Does.Contain("schema export --output-format"));
        Assert.That((string?)JObject.Parse(canonical.Output)["properties"]!["id"]!["type"], Is.EqualTo("integer"));
    }

    [Test]
    public async Task Diff_PerInputFormatsOverrideTheSharedInputFormat()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", CliWorkspace.PackageYaml);
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--input-format", "odcs",
            "--baseline-format", "datapackage", "--candidate-format", "auto", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["hasChanges"], Is.False);
    }

    [Test]
    public async Task QuietDiff_DoesNotCreateAnOutputFile()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("descriptor", CliWorkspace.Odcs);
        var output = Path.Combine(workspace.Path, "quiet.json");
        var result = await CliWorkspace.Run("contract", "diff", source, source, "--quiet", "--output-format", "json", "-o", output);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Is.Empty);
        Assert.That(File.Exists(output), Is.False);
    }

    [Test]
    public async Task Validation_CanSaveAnInvalidResultWithoutReplacingAnExistingFile()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("broken.json", "{broken");
        var output = Path.Combine(workspace.Path, "invalid.json");
        var first = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json", "-o", output);
        Assert.That(first.ExitCode, Is.EqualTo(1), first.Error);
        Assert.That((bool?)JObject.Parse(File.ReadAllText(output))["valid"], Is.False);
        var second = await CliWorkspace.Run("contract", "validate", source, "--output-format", "json", "-o", output);
        Assert.That(second.ExitCode, Is.EqualTo(3), second.Error);
    }

    [Test]
    public async Task InvalidPreviewLimit_ReportsTheOptionWithoutEchoingInput()
    {
        var result = await CliWorkspace.Run("data", "preview", "missing", "--asset", "customers", "--limit", "DO-NOT-EMIT");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Does.Contain("--limit").And.Not.Contain("DO-NOT-EMIT"));
    }

    [TestCase("contract")]
    [TestCase("schema")]
    [TestCase("data")]
    [TestCase("dialect")]
    [TestCase("connector")]
    [TestCase("config")]
    public async Task BareBranches_ShowHelp(string command)
    {
        var result = await CliWorkspace.Run(command);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("USAGE"));
    }

    private static string[] Arguments(string command, string source) => command switch
    {
        "contract|compare" => ["contract", "compare", source, source, "--fail-on", "none"],
        "contract|diff" => ["contract", "diff", source, source],
        "data|preview" => ["data", "preview", source, "--asset", "customers"],
        "schema|infer" => ["schema", "infer", source, "--asset", "customers"],
        "schema|export" => ["schema", "export", source, "--asset", "customers"],
        "dialect|export" => ["dialect", "export", source, "--endpoint", "local"],
        "dialect|infer" => ["dialect", "infer", source, "--asset", "customers"],
        _ => [.. command.Split('|'), source]
    };
}
