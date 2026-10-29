using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class DialectInferCommandTests
{
    private const string Csv = "id;amount\r\n1;42\r\n2;35\r\n";
    private const string Json = """
        {"name":"sample","title":"Keep me","customDate":"2026-10-29T14:05:06.000+02:00","customNumber":0.12345678901234567890123456789,"resources":[
          {"name":"customers","path":"customers.csv","format":"csv","dialect":{"header":true,"nullSequence":"NA","custom":"keep"},
           "schema":{"fields":[{"name":"authored","type":"string","constraints":{"required":true,"pattern":"[A-Z]+"},"custom":"keep"}],"primaryKey":["authored"]}},
          {"name":"other","path":"other.csv","schema":{"fields":[{"name":"unrelated","type":"string"}]}}
        ]}
        """;
    private const string Yaml = """
        name: sample
        title: Keep me
        resources:
          - name: customers
            path: customers.csv
            format: csv
            dialect:
              type: delimited
              header: true
              nullSequence: NA
            schema:
              fields:
                - name: authored
                  type: string
                  constraints:
                    required: true
                    pattern: '[A-Z]+'
          - name: other
            path: other.csv
        """;
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: customer-contract
        version: 1.0.0
        status: draft
        schema:
          - name: customers
            physicalType: file
            properties:
              - name: authored
                logicalType: string
                required: true
                description: Keep me
        servers:
          - server: local
            type: local
            path: customers.csv
            format: csv
            customProperties:
              - vendor: other
                property: note
                value: keep
        """;

    [TestCase("json", "report")]
    [TestCase("yaml", "report")]
    [TestCase("odcs", "report")]
    [TestCase("json", "output")]
    [TestCase("yaml", "output")]
    [TestCase("odcs", "output")]
    [TestCase("json", "write")]
    [TestCase("yaml", "write")]
    [TestCase("odcs", "write")]
    public async Task Infer_PreservesEverythingExceptSelectedDialect(string standard, string mode)
    {
        using var workspace = new CliWorkspace();
        var content = Content(standard);
        var source = workspace.Write("contract." + (standard == "json" ? "json" : "yaml"), content);
        var csv = workspace.Write("customers.csv", Csv);
        var output = Path.Combine(workspace.Path, "enriched");
        string[] options = mode switch { "output" => ["-o", output], "write" => ["--write"], _ => [] };
        var result = await CliWorkspace.Run(["dialect", "infer", source, "--asset", "customers", "--header", "present", "--accept-ambiguous", .. options]);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("Sampled records: 3").And.Contain("confidence").And.Contain("delimiter ';'"));
        Assert.That(result.Output, Does.Not.Contain("Logical type"));
        Assert.That(File.ReadAllText(csv), Is.EqualTo(Csv));
        if (mode == "report")
        {
            Assert.That(File.ReadAllText(source), Is.EqualTo(content));
            Assert.That(Directory.GetFiles(workspace.Path), Has.Length.EqualTo(2));
            return;
        }
        if (mode == "output") Assert.That(File.ReadAllText(source), Is.EqualTo(content));
        var updated = File.ReadAllText(mode == "output" ? output : source);
        Assert.That(result.Output, Does.Contain("Reserialization").And.Contain("comments").And.Contain("aliases"));
        AssertPreserved(standard, content, updated);
        using var reloaded = ContractDocument.Load(mode == "output" ? output : source);
        var endpoint = InferenceService.SelectEndpoint(reloaded.Contract, InferenceService.SelectAsset(reloaded.Contract, "customers"));
        Assert.That(endpoint.Format!.Options["delimiter"]!.ToString(), Is.EqualTo(";"));
        Assert.That(endpoint.Format.Options["lineTerminator"], Is.EqualTo("\r\n"));
    }

    [Test]
    public async Task Infer_AmbiguityNeedsAcknowledgementAndExistingOutputIsProtected()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Json);
        workspace.Write("customers.csv", Csv);
        var output = workspace.Write("existing.json", "keep");
        var ambiguous = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--write");
        Assert.That(ambiguous.ExitCode, Is.EqualTo(1));
        Assert.That(ambiguous.Error, Does.Contain("Ambiguous"));
        var existing = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--accept-ambiguous", "--output", output);
        Assert.That(existing.ExitCode, Is.EqualTo(1));
        Assert.That(existing.Error, Does.Contain("already exists"));
        Assert.That(File.ReadAllText(output), Is.EqualTo("keep"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Json));
    }

    [TestCase("", "No CSV records")]
    [TestCase("id;amount", "Insufficient")]
    [TestCase(Csv, "Insufficient", "--maximum-bytes", "10")]
    [TestCase(Csv, "Insufficient", "--maximum-records", "1")]
    public async Task Infer_InsufficientSampleCannotReplaceSource(string csv, string error, params string[] options)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Json);
        workspace.Write("customers.csv", csv);
        var result = await CliWorkspace.Run(["dialect", "infer", source, "--asset", "customers", "--write", "--accept-ambiguous", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain(error));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Json));
    }

    [Test]
    public async Task Infer_SharedEndpointRequiresExplicitAcknowledgement()
    {
        using var workspace = new CliWorkspace();
        var content = Odcs.Replace("servers:", "  - name: second\n    physicalType: file\nservers:");
        var source = workspace.Write("contract.yaml", content);
        workspace.Write("customers.csv", Csv);
        var report = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--header", "present");
        Assert.That(report.ExitCode, Is.Zero, report.Error);
        Assert.That(report.Output, Does.Contain("Shared endpoint affects assets: customers, second"));
        var refused = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--header", "present", "--write", "--accept-ambiguous");
        Assert.That(refused.ExitCode, Is.EqualTo(1));
        Assert.That(refused.Error, Does.Contain("--allow-shared-endpoint"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(content));
        var allowed = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--header", "present", "--write", "--accept-ambiguous", "--allow-shared-endpoint");
        Assert.That(allowed.ExitCode, Is.Zero, allowed.Error);
        AssertPreserved("odcs", content, File.ReadAllText(source));
    }

    [Test]
    [Platform("Win")]
    public async Task Infer_FailedAtomicReplacementLeavesOriginalIntact()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Json);
        workspace.Write("customers.csv", Csv);
        using (var held = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--write", "--accept-ambiguous");
            Assert.That(result.ExitCode, Is.EqualTo(1));
        }
        Assert.That(File.ReadAllText(source), Is.EqualTo(Json));
        Assert.That(Directory.GetFiles(workspace.Path, "*.tmp"), Is.Empty);
    }

    [TestCase("--write", "--output", "unused")]
    [TestCase("--maximum-records", "0")]
    [TestCase("--maximum-bytes", "0")]
    [TestCase("--header", "invalid")]
    public async Task Infer_InvalidOptionsFailBeforeReading(params string[] options)
    {
        var result = await CliWorkspace.Run(["dialect", "infer", "missing", "--asset", "customers", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2));
    }

    [TestCase("missing", "csv", "not found")]
    [TestCase("customers", "parquet", "supported CSV")]
    [TestCase("customers", "csv", "could not find")]
    public async Task Infer_ReportsSelectionAndResourceFailures(string asset, string format, string error)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Json.Replace("\"format\":\"csv\"", $"\"format\":\"{format}\""));
        var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", asset, "--write");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error.ToLowerInvariant(), Does.Contain(error.ToLowerInvariant()));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Json.Replace("\"format\":\"csv\"", $"\"format\":\"{format}\"")));
    }

    [Test]
    public async Task Infer_HelpAndSourceRestrictions()
    {
        var help = await CliWorkspace.Run("dialect", "infer", "--help");
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("--asset").And.Contain("--write").And.Contain("--maximum-bytes").And.Contain("--allow-shared-endpoint"));
        foreach (var source in new[] { "https://example.invalid/contract.json", "package.zip" })
        {
            var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers");
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Error, Does.Contain("ordinary local"));
        }
    }

    [TestCase("duplicate")]
    [TestCase("bindings")]
    public async Task Infer_RejectsAmbiguousSelection(string scenario)
    {
        using var workspace = new CliWorkspace();
        var content = scenario == "duplicate"
            ? Odcs.Replace("servers:", "  - name: customers\n    physicalType: file\nservers:")
            : Odcs + "\n  - server: other\n    type: local\n    path: other.csv\n    format: csv\n";
        var source = workspace.Write("contract.yaml", content);
        workspace.Write("customers.csv", Csv);
        var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--write");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain(scenario == "duplicate" ? "ambiguous" : "exactly one endpoint binding"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(content));
    }

    [TestCase("anchor", "title: &title Keep me")]
    [TestCase("tag", "title: !!str Keep me")]
    public async Task Infer_RefusesPotentiallyLossyYamlUpdates(string scenario, string title)
    {
        using var workspace = new CliWorkspace();
        var content = Yaml.Replace("title: Keep me", title);
        var source = workspace.Write(scenario + ".yaml", content);
        workspace.Write("customers.csv", Csv);
        var report = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers");
        Assert.That(report.ExitCode, Is.Zero, report.Error);
        var write = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--write", "--accept-ambiguous");
        Assert.That(write.ExitCode, Is.EqualTo(1));
        Assert.That(write.Error, Does.Contain("Expand these constructs"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(content));
    }

    [TestCase("tsv", "\t", "\n")]
    [TestCase("psv", "|", "\r\n")]
    public async Task Infer_DetectsNonDefaultFamilyAndLineEndings(string format, string separator, string newline)
    {
        using var workspace = new CliWorkspace();
        var content = CliWorkspace.PackageJson.Replace("customers.csv", "customers." + format)
            .Replace("\"format\":\"csv\"", "\"format\":\"" + format + "\"");
        var source = workspace.Write("contract.json", content);
        workspace.Write("customers." + format, string.Join(newline, new[] { "id" + separator + "name", "1" + separator + "Alice", "2" + separator + "Bob", "" }));
        var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--write", "--accept-ambiguous");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var dialect = JObject.Parse(File.ReadAllText(source))["resources"]![0]!["dialect"]!;
        Assert.That((string?)dialect["delimiter"], Is.EqualTo(separator));
        Assert.That((string?)dialect["lineTerminator"], Is.EqualTo(newline));
    }

    [TestCase("auto", true)]
    [TestCase("absent", false)]
    public async Task Infer_ExplicitHeaderOverridesDeclaredHint(string header, bool expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", CliWorkspace.PackageJson);
        workspace.Write("customers.csv", "1;42\n2;35\n");
        var result = await CliWorkspace.Run("dialect", "infer", source, "--asset", "customers", "--header", header, "--write", "--accept-ambiguous");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var dialect = JObject.Parse(File.ReadAllText(source))["resources"]![0]!["dialect"]!;
        Assert.That((bool?)dialect["header"], Is.EqualTo(expected));
        Assert.That(dialect["headerRows"]!.Values<int>(), Is.EqualTo(expected ? new[] { 1 } : Array.Empty<int>()));
    }

    private static string Content(string standard) => standard switch { "json" => Json, "yaml" => Yaml, _ => Odcs };

    private static void AssertPreserved(string standard, string before, string after)
    {
        if (standard == "json")
        {
            var original = JObject.Parse(before);
            var updated = JObject.Parse(after);
            using var json = System.Text.Json.JsonDocument.Parse(after);
            Assert.That(json.RootElement.GetProperty("customDate").GetString(), Is.EqualTo("2026-10-29T14:05:06.000+02:00"));
            Assert.That(json.RootElement.GetProperty("customNumber").GetRawText(), Is.EqualTo("0.12345678901234567890123456789"));
            var dialect = updated["resources"]![0]!["dialect"]!;
            Assert.That((string?)dialect["nullSequence"], Is.EqualTo("NA"));
            Assert.That((string?)dialect["custom"], Is.EqualTo("keep"));
            updated["resources"]![0]!["dialect"] = original["resources"]![0]!["dialect"]!.DeepClone();
            Assert.That(JToken.DeepEquals(original, updated), Is.True, "Only the selected dialect may change");
            return;
        }
        var source = LoadYaml(before);
        var target = LoadYaml(after);
        if (standard == "odcs")
        {
            var originalServer = (YamlMappingNode)((YamlSequenceNode)source["servers"])[0];
            var updatedServer = (YamlMappingNode)((YamlSequenceNode)target["servers"])[0];
            var properties = (YamlSequenceNode)updatedServer["customProperties"];
            Assert.That(properties.Children.OfType<YamlMappingNode>().Count(item => ((YamlScalarNode)item["vendor"]).Value == "other"), Is.EqualTo(1));
            updatedServer.Children[new YamlScalarNode("customProperties")] = originalServer["customProperties"];
        }
        else
        {
            var originalResource = (YamlMappingNode)((YamlSequenceNode)source["resources"])[0];
            var updatedResource = (YamlMappingNode)((YamlSequenceNode)target["resources"])[0];
            Assert.That(((YamlScalarNode)((YamlMappingNode)updatedResource["dialect"])["nullSequence"]).Value, Is.EqualTo("NA"));
            updatedResource.Children[new YamlScalarNode("dialect")] = originalResource["dialect"];
        }
        Assert.That(target.Equals(source), Is.True, "Only selected endpoint dialect values may change");
    }

    private static YamlMappingNode LoadYaml(string content)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(content));
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }
}
