using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli.Testing;

public class QualityCommandTests
{
    private const string Contract = """
        apiVersion: v3.2.0
        kind: DataContract
        id: customer-contract
        name: Customer Contract
        version: 1.4.2
        status: draft
        schema:
          - name: customers
            physicalType: file
            quality:
              - id: z-count
                type: library
                metric: rowCount
                mustBe: 2
                dimension: completeness
              - type: text
                description: Manual review
            properties:
              - name: email
                logicalType: string
                quality:
                  - id: a-missing
                    type: library
                    metric: nullValues
                    mustBe: 0
                    dimension: completeness
        servers:
          - server: local
            type: local
            path: customers.csv
            format: csv
        """;

    private static JToken Property(JToken container, string name) => container["customProperties"]!.Single(p => (string?)p["property"] == name)["value"]!;

    [TestCase("positive", 0, "pass")]
    [TestCase("negative", 1, "fail")]
    public async Task CustomExpressifRulesDispatchThroughTheRegisteredEngine(string program, int code, string status)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("custom.yaml", $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: custom-quality
            version: 1.0.0
            status: active
            schema:
              - name: customers
                properties:
                  - name: value
                    logicalType: integer
                    quality:
                      - type: custom
                        engine: expressif
                        implementation: '{"version":"1","kind":"predicate","scope":"value","program":"{{program}}"}'
            servers:
              - server: local
                type: local
                path: customers.csv
                format: csv
            """);
        workspace.Write("customers.csv", "value\r\n1\r\n2\r\n");
        var result = await CliWorkspace.Run("quality", "run", source);
        Assert.That(result.ExitCode, Is.EqualTo(code), result.Error + result.Output);
        var doc = JObject.Parse(result.Output);
        Assert.That((string?)doc["results"]![0]!["status"], Is.EqualTo(status));
        Assert.That((string?)Property(doc, "engines")[0]!["types"]![0], Is.EqualTo("custom"));
        var invalid = workspace.Write("invalid.yaml", File.ReadAllText(source).Replace("\"version\":\"1\"", "\"version\":\"99\""));
        var error = await CliWorkspace.Run("quality", "run", invalid);
        Assert.That(error.ExitCode, Is.EqualTo(2));
        Assert.That((string?)JObject.Parse(error.Output)["results"]![0]!["status"], Is.EqualTo("error"));
    }

    [Test]
    public async Task QualityHelpAndMissingArgumentsUseTheDocumentedCommandFamily()
    {
        var branch = await CliWorkspace.Run("quality");
        var help = await CliWorkspace.Run("quality", "run", "--help");
        Assert.That(branch.ExitCode, Is.Zero);
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("--asset").And.Contain("--field").And.Contain("--id").And.Contain("--dimension").And.Contain("--type").And.Contain("--output-format"));
        Assert.That((await CliWorkspace.Run("quality", "run")).ExitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task RunProducesValidatedOorsInContractOrderWithTimingsProvenanceAndOnlyParticipatingEngines()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.odcs.yaml", Contract);
        var data = workspace.Write("customers.csv", "email\r\na@example.test\r\nb@example.test\r\n");
        var result = await CliWorkspace.Run("quality", "run", source);
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        var document = JObject.Parse(result.Output);
        OorsReport.Validate(document);
        Assert.Multiple(() =>
        {
            Assert.That((string?)document["apiVersion"], Is.EqualTo("v0.8.0"));
            Assert.That((string?)document["kind"], Is.EqualTo("ObservabilityResults"));
            Assert.That(document["results"]!.Select(r => (string?)r["status"]), Is.EqualTo(new[] { "pass", "skip", "pass" }));
            Assert.That(document["results"]![0]!["id"]!.Value<string>(), Is.EqualTo("z-count"));
            Assert.That(document["results"]![2]!["id"]!.Value<string>(), Is.EqualTo("a-missing"));
            Assert.That(document["id"]!.Value<string>(), Is.Not.EqualTo(document["source"]!["runId"]!.Value<string>()));
            Assert.That((string?)document["contract"]!["server"], Is.EqualTo("local"));
            Assert.That(Property(document, "engines").Count(), Is.EqualTo(1));
            Assert.That((string?)Property(document, "engines")[0]!["id"], Is.EqualTo("packata.rules.expressif"));
            Assert.That((int?)Property(document, "summary")["pass"], Is.EqualTo(2));
            Assert.That((int?)Property(document, "summary")["skip"], Is.EqualTo(1));
            Assert.That((string?)Property(document, "contractProvenance")["fingerprint"], Does.StartWith("sha256:"));
            Assert.That((string?)Property(document, "contractProvenance")["specification"], Is.EqualTo("3.2.0"));
            Assert.That(Property(document, "runTiming")["duration"]!.Value<string>(), Does.EndWith("s"));
            Assert.That(document["results"]![1]!["customProperties"]!.Any(p => (string?)p["property"] == "duration"), Is.False);
            Assert.That(document["results"]![1]!["customProperties"]!.Any(p => (string?)p["property"] == "ruleId"), Is.False);
            Assert.That(document.SelectTokens("$..customProperties[*].vendor").Values<string>(), Is.All.EqualTo("packata"));
            Assert.That(File.ReadAllText(source), Is.EqualTo(Contract));
        });
        using var exclusive = File.Open(data, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task FiltersUseAndOrAndDoNotEmitNonSelectedRulesOrUnusedEngines()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Contract);
        workspace.Write("customers.csv", "email\r\na\r\nb\r\n");
        var result = await CliWorkspace.Run("quality", "run", source, "--asset", "customers", "--field", "email", "--dimension", "completeness",
            "--dimension", "uniqueness", "--type", "library", "--id", "a-missing", "--id", "unknown");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var doc = JObject.Parse(result.Output);
        Assert.That(doc["results"]!.Count(), Is.EqualTo(1));
        Assert.That(Property(doc, "filters")["dimension"]!.Values<string>(), Is.EqualTo(new[] { "completeness", "uniqueness" }));
        Assert.That(Property(doc, "filters")["asset"]!.Type, Is.EqualTo(JTokenType.Array));
        Assert.That((int?)Property(doc, "summary")["skip"], Is.Zero);
        var text = await CliWorkspace.Run("quality", "run", source, "--type", "text");
        Assert.That(text.ExitCode, Is.EqualTo(2));
        Assert.That(Property(JObject.Parse(text.Output), "engines"), Is.Empty);
    }

    [TestCase("--field|email")]
    [TestCase("--id|absent")]
    [TestCase("--output-format|text")]
    [TestCase("--output-format|csv")]
    public async Task InvalidSelectionAndArgumentsProduceCommandErrorWithoutAReport(string arguments)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Contract);
        var result = await CliWorkspace.Run(["quality", "run", source, ..arguments.Split('|')]);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Is.Not.Empty);
    }

    [Test]
    public async Task QualityFailureAndMissingInputHaveDistinctExitCodesAndStillExportOors()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Contract.Replace("mustBe: 2", "mustBe: 99"));
        workspace.Write("customers.csv", "email\r\na\r\nb\r\n");
        var failed = await CliWorkspace.Run("quality", "run", source, "--id", "z-count");
        Assert.That(failed.ExitCode, Is.EqualTo(1), failed.Error);
        Assert.That((string?)Property(JObject.Parse(failed.Output), "summary")["outcome"], Is.EqualTo("fail"));
        var missing = workspace.Write("missing.yaml", Contract.Replace("customers.csv", "absent.csv"));
        var error = await CliWorkspace.Run("quality", "run", missing, "--id", "z-count");
        Assert.That(error.ExitCode, Is.EqualTo(2));
        Assert.That((string?)Property(JObject.Parse(error.Output), "summary")["outcome"], Is.EqualTo("error"));
    }

    [Test]
    public async Task YamlOutputRemainsOorsAndRefusesToOverwriteFiles()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Contract);
        var output = Path.Combine(workspace.Path, "report.oors.yaml");
        var result = await CliWorkspace.Run("quality", "run", source, "--type", "text", "--output-format", "yaml", "--output", output);
        Assert.That(result.ExitCode, Is.EqualTo(2)); // all skipped
        Assert.That(result.Output, Is.Empty);
        var saved = File.ReadAllText(output);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(saved));
        var map = (YamlMappingNode)yaml.Documents[0].RootNode;
        Assert.That(((YamlScalarNode)map.Children[new YamlScalarNode("apiVersion")]).Value, Is.EqualTo("v0.8.0"));
        var again = await CliWorkspace.Run("quality", "run", source, "--type", "text", "--output", output);
        Assert.That(again.ExitCode, Is.EqualTo(2));
        Assert.That(again.Error, Is.Not.Empty);
        Assert.That(File.ReadAllText(output), Is.EqualTo(saved));
    }

    [Test]
    public async Task InvalidContractAndMalformedInputDoNotLeakParserContent()
    {
        using var workspace = new CliWorkspace();
        foreach (var content in new[] { Contract.Replace("v3.2.0", "v0.0.0"), "apiVersion: [\npassword: do-not-export\n", CliWorkspace.PackageYaml })
        {
            var source = workspace.Write("contract.yaml", content);
            var result = await CliWorkspace.Run("quality", "run", source);
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Does.Not.Contain("do-not-export"));
        }
    }

    [TestCase(RuleOutcome.Passed, "pass")]
    [TestCase(RuleOutcome.Failed, "fail")]
    [TestCase(RuleOutcome.Warn, "warn")]
    [TestCase(RuleOutcome.Error, "error")]
    [TestCase(RuleOutcome.Skipped, "skip")]
    public void ExportPreservesTypedOutputsAndOorsStatusesWithVendorProperties(RuleOutcome outcome, string status)
    {
        foreach (var value in new object[] { 153, false, new Dictionary<string, object?> { ["invalidRows"] = 153, ["totalRows"] = 8500 } })
        {
            var doc = Export(value, outcome);
            OorsReport.Validate(doc);
            Assert.That((string?)doc["results"]![0]!["status"], Is.EqualTo(status));
            Assert.That((int?)Property(doc, "summary")[status], Is.EqualTo(1));
            if (value is int) Assert.That((int?)doc["results"]![0]!["measure"]!["value"], Is.EqualTo(153));
            else Assert.That(Property(doc["results"]![0]!, "output").Type, Is.EqualTo(value is bool ? JTokenType.Boolean : JTokenType.Object));
        }
    }

    [Test]
    public void ExportSanitizesAndBoundsOutputsAndValidatesTheActualSchema()
    {
        var doc = Export(new Dictionary<string, object?> { ["password"] = "do-not-export", ["count"] = 3 });
        Assert.That(doc.ToString(), Does.Not.Contain("do-not-export"));
        var large = Export(new string('a', 100_000));
        Assert.That(large["results"]![0]!["measure"]!["value"]!.Value<string>()!.Length, Is.LessThan(4200));
        Assert.That(OorsReport.SafeSource("https://name:do-not-export@example.test/contract.yaml?token=do-not-export#secret"), Is.EqualTo("https://example.test/contract.yaml"));
        doc["results"]![0]!["unexpected"] = true;
        Assert.Throws<InvalidOperationException>(() => OorsReport.Validate(doc));
        doc = Export(1);
        doc["observedAt"] = "invalid";
        Assert.Throws<InvalidOperationException>(() => OorsReport.Validate(doc));
        Assert.That(OorsReport.Fingerprint("name: hello\nversion: 1\n"), Is.EqualTo(OorsReport.Fingerprint("version: 1\nname: hello\n")));
        Assert.That(OorsReport.Fingerprint("name: hello"), Is.Not.EqualTo(OorsReport.Fingerprint("name: changed")));
    }

    [Test]
    public void AnonymousAndDuplicateIdsAreDeterministicAndCannotCollideWithNativeIds()
    {
        var doc = Export(1, duplicateIds: true);
        var ids = doc["results"]!.Select(r => (string)r["id"]!).ToArray();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(3));
        Assert.That(Export(1, duplicateIds: true)["results"]!.Select(r => (string)r["id"]!), Is.EqualTo(ids));
    }

    private static JObject Export(object value, RuleOutcome outcome = RuleOutcome.Passed, bool duplicateIds = false)
    {
        var now = DateTimeOffset.UtcNow;
        DataQualityRule Rule(string? id, string path) => new("sql") { SourceId = id, SourcePath = path, Target = new("customers", ["email"]),
            Definition = new SqlQuality("not exported"), Metadata = new(Description: "Check email", Dimension: "completeness"),
            Expectations = [new(ExpectationOperator.Equal, QualityValue.From(0))] };
        QualityRunResult Result(DataQualityRule rule) => new(rule, outcome, new(QualityCompilation.Descriptor(rule, "sql", "1", "production"),
            outcome, new(QualityValue.From(value)), rule.Expectations, now, now, now, TimeSpan.FromMilliseconds(1)), null, "packata.rules.sql");
        var results = duplicateIds ? new[] { Result(Rule("duplicate", "q/0")), Result(Rule("duplicate", "q/1")), Result(Rule("q/0", "q/2")) }
            : new[] { Result(Rule(null, "schema/customers/quality/0")) };
        var contract = new DataContract(new("contract", Version: "1"), new(), [], [], new());
        var source = new ContractSource("contract.yaml", new Uri("file:///"), new object()) { OriginalText = "id: contract" };
        return OorsReport.Create(new("run", now, now, TimeSpan.Zero, results, [new("packata.rules.sql", "1", ["sql"])]), contract, "v3.2.0", source, new());
    }
}
