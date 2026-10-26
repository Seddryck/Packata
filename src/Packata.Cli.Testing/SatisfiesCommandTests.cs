using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

public class SatisfiesCommandTests
{
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        version: 1.0.0
        status: active
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: string
                required: true
                nullable: false
                logicalTypeOptions:
                  maxLength: 50
        """;
    private const string Package = """
        {"name":"sales","resources":[{"name":"orders","path":"must-not-open.csv","schema":{"fields":[{"name":"id","type":"string","constraints":{"required":true,"maxLength":50}}]}}]}
        """;

    [TestCase(Odcs)]
    [TestCase(Package)]
    public async Task Complete_native_documents_have_independent_roles_and_backend_parity(string native)
    {
        using var workspace = new CliWorkspace();
        var producer = workspace.Write("producer", native.Replace("sales", "producer"));
        var consumer = workspace.Write("consumer", native.Replace("sales", "consumer"));
        var result = await CliWorkspace.Run("contract", "satisfies", producer, "--consumer", consumer, "--format", "json");
        var again = await CliWorkspace.Run("contract", "satisfies", producer, "--consumer", consumer, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        var json = JObject.Parse(result.Output);
        Assert.That((int?)json["schemaVersion"], Is.EqualTo(1));
        Assert.That((bool?)json["isSatisfied"], Is.True);
        Assert.That((bool?)json["completed"], Is.True);
        Assert.That((string?)json["producer"]?["identity"]?["id"], Is.EqualTo("producer"));
        Assert.That((string?)json["consumer"]?["identity"]?["id"], Is.EqualTo("consumer"));
        Assert.That(again.Output, Is.EqualTo(result.Output));
        using var p = await ContractSource.LoadAsync(producer, "auto", default);
        using var c = await ContractSource.LoadAsync(consumer, "auto", default);
        static SatisfactionInput Input(ContractSource source)
        {
            var input = source.CompatibilityInput();
            return new(input.Contract, input.Standard, source.Source, input.Diagnostics, input.Descriptor);
        }
        var backend = ContractSatisfactionEvaluator.EvaluateSatisfaction(Input(p), Input(c), SatisfactionPolicy.Default);
        Assert.That(json["findings"]!.Select(x => (string?)x["code"]), Is.EqualTo(backend.Findings.Select(x => x.Code)));
        Assert.That(File.ReadAllText(producer), Is.EqualTo(native.Replace("sales", "producer")));
    }

    [TestCase(false, "unsatisfied", false, false, 0)]
    [TestCase(false, "ignore", false, false, 0)]
    [TestCase(false, "error", false, false, 0)]
    [TestCase(false, "unsatisfied", true, false, 1)]
    [TestCase(false, "ignore", true, false, 0)]
    [TestCase(false, "error", true, false, 2)]
    [TestCase(false, "unsatisfied", false, true, 1)]
    [TestCase(false, "ignore", false, true, 1)]
    [TestCase(false, "error", false, true, 1)]
    [TestCase(false, "unsatisfied", true, true, 1)]
    [TestCase(false, "ignore", true, true, 1)]
    [TestCase(false, "error", true, true, 2)]
    [TestCase(true, "unsatisfied", false, false, 0)]
    [TestCase(true, "ignore", false, false, 0)]
    [TestCase(true, "error", false, false, 0)]
    [TestCase(true, "unsatisfied", true, false, 1)]
    [TestCase(true, "ignore", true, false, 0)]
    [TestCase(true, "error", true, false, 2)]
    [TestCase(true, "unsatisfied", false, true, 1)]
    [TestCase(true, "ignore", false, true, 1)]
    [TestCase(true, "error", false, true, 1)]
    [TestCase(true, "unsatisfied", true, true, 1)]
    [TestCase(true, "ignore", true, true, 1)]
    [TestCase(true, "error", true, true, 2)]
    public async Task Unknown_policy_matrix_preserves_findings_and_quiet_codes(bool package, string policy, bool unknown, bool unsatisfied, int expected)
    {
        using var workspace = new CliWorkspace();
        var native = package ? Package : Odcs;
        var producer = workspace.Write("producer", unsatisfied ? native.Replace("50", "100") : native);
        var consumer = workspace.Write("consumer", unknown ? AddUnknown(native, package) : native);
        var args = new[] { "contract", "satisfies", producer, "--consumer", consumer, "--unknown", policy, "--format", "json" };
        var result = await CliWorkspace.Run(args);
        Assert.That(result.ExitCode, Is.EqualTo(expected), result.Output + result.Error);
        var json = JObject.Parse(result.Output);
        Assert.That((bool?)json["completed"], Is.True);
        Assert.That((bool?)json["isSatisfied"], Is.EqualTo(!unknown && !unsatisfied));
        Assert.That(json["findings"]!.Any(x => (string?)x["status"] == "Unknown"), Is.EqualTo(unknown));
        var quiet = await CliWorkspace.Run([.. args, "--quiet"]);
        Assert.That(quiet.ExitCode, Is.EqualTo(expected));
        Assert.That(quiet.Output + quiet.Error, Is.Empty);
    }

    private static string AddUnknown(string native, bool package) => package
        ? native.Replace("\"maxLength\":50", "\"maxLength\":50,\"pattern\":\".*\"")
        : native + "\ncustomContractSemantics: unsupported\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task All_native_serializations_work_and_lengths_are_directional(bool package)
    {
        using var workspace = new CliWorkspace();
        var native = package ? Package : Odcs;
        // YAML is used for both standards; ODCS also accepts JSON through the existing parser.
        var objectTree = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(native);
        var yaml = new YamlDotNet.Serialization.SerializerBuilder().Build().Serialize(objectTree);
        var json = new YamlDotNet.Serialization.SerializerBuilder().JsonCompatible().Build().Serialize(objectTree);
        var producer = workspace.Write("producer.json", json.Replace("50", "30"));
        var consumer = workspace.Write("consumer.yaml", yaml);
        Assert.That((await CliWorkspace.Run("contract", "satisfies", producer, "--consumer", consumer)).ExitCode, Is.Zero);
        Assert.That((await CliWorkspace.Run("contract", "satisfies", consumer, "--consumer", producer)).ExitCode, Is.EqualTo(1));
    }

    [Test]
    public async Task Native_defaults_distinguish_optional_fields_and_nullability()
    {
        using var workspace = new CliWorkspace();
        var p = workspace.Write("producer", Odcs.Replace("nullable: false", "nullable: true"));
        var c = workspace.Write("consumer", Odcs);
        Assert.That((await CliWorkspace.Run("contract", "satisfies", p, "--consumer", c)).ExitCode, Is.EqualTo(1));
        Assert.That((await CliWorkspace.Run("contract", "satisfies", c, "--consumer", p)).ExitCode, Is.Zero);
        var noBound = workspace.Write("unbounded", Odcs.Replace("maxLength: 50", "minLength: 0"));
        var result = await CliWorkspace.Run("contract", "satisfies", noBound, "--consumer", c, "--format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(JObject.Parse(result.Output)["findings"]!.Any(x => (string?)x["code"] == "SAT120:maxLength" && (string?)x["status"] == "Unsatisfied"), Is.True);
    }

    [Test]
    public async Task Invalid_native_parameters_and_cross_standard_inputs_return_structured_failure()
    {
        using var workspace = new CliWorkspace();
        var odcs = workspace.Write("odcs", Odcs);
        var package = workspace.Write("package", Package);
        var invalid = workspace.Write("invalid", Odcs.Replace("maxLength: 50", "precision: 2\n                  scale: 3"));
        foreach (var pair in new[] { (odcs, package), (invalid, odcs), (odcs, invalid), (odcs, "missing-file") })
        {
            var result = await CliWorkspace.Run("contract", "satisfies", pair.Item1, "--consumer", pair.Item2, "--unknown", "ignore", "--format", "json");
            Assert.That(result.ExitCode, Is.EqualTo(2), result.Output + result.Error);
            Assert.That((bool?)JObject.Parse(result.Output)["completed"], Is.False);
            Assert.That((bool?)JObject.Parse(result.Output)["isSatisfied"], Is.False);
        }
    }

    [TestCase("--unknown", "unsafe")]
    [TestCase("--format", "xml")]
    [TestCase("--requirements", "old-document")]
    public async Task Invalid_invocations_have_failure_json_and_quiet_is_silent(string option, string value)
    {
        var args = new[] { "contract", "satisfies", "missing", "--consumer", "missing", option, value, "--output-format", "json" };
        var result = await CliWorkspace.Run(args);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Output + result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["completed"], Is.False);
        var quiet = await CliWorkspace.Run([.. args, "--quiet"]);
        Assert.That(quiet.ExitCode, Is.EqualTo(2));
        Assert.That(quiet.Output + quiet.Error, Is.Empty);
    }

    [Test]
    public async Task Cancellation_and_redaction_use_shared_CLI_conventions()
    {
        var settings = new SatisfiesSettings { Producer = "not-opened", Consumer = "not-opened", Quiet = true };
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await new SatisfiesCommand().ExecuteAsync(null!, settings, cancellation.Token);
        Assert.That(cancelled, Is.EqualTo(2));
        using var workspace = new CliWorkspace();
        var native = Odcs + "\ncustom: token=do-not-leak\n";
        var producer = workspace.Write("token=do-not-leak", native);
        var consumer = workspace.Write("consumer", native);
        var result = await CliWorkspace.Run("contract", "satisfies", producer, "--consumer", consumer, "--format", "json", "--unknown", "ignore");
        Assert.That(result.Output + result.Error, Does.Not.Contain("do-not-leak"));
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That((bool?)JObject.Parse(result.Output)["isSatisfied"], Is.False);
    }
    [TestCase(10, 2, 12, 4, 0)]
    [TestCase(12, 2, 12, 4, 1)]
    [TestCase(12, 4, 10, 2, 1)]
    public async Task Native_decimal_precision_and_scale_are_normalized(int pp, int ps, int cp, int cs, int exit)
    {
        using var workspace = new CliWorkspace();
        static string Decimal(int precision, int scale) => Odcs.Replace("logicalType: string", "logicalType: number")
            .Replace("maxLength: 50", $"precision: {precision}\n          scale: {scale}");
        var producer = workspace.Write("producer", Decimal(pp, ps));
        var consumer = workspace.Write("consumer", Decimal(cp, cs));
        var result = await CliWorkspace.Run("contract", "satisfies", producer, "--consumer", consumer, "--format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(exit), result.Output + result.Error);
        var findings = JObject.Parse(result.Output)["findings"]!;
        Assert.That(findings.Count(x => ((string?)x["code"])?.StartsWith("SAT121:", StringComparison.Ordinal) == true), Is.EqualTo(2));
        var incomplete = workspace.Write("incomplete", Decimal(pp, ps).Replace($"          scale: {ps}", ""));
        var unknown = await CliWorkspace.Run("contract", "satisfies", incomplete, "--consumer", consumer, "--unknown", "error");
        Assert.That(unknown.ExitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task Missing_consumer_option_and_failed_input_stay_errors_under_ignore()
    {
        var missing = await CliWorkspace.Run("contract", "satisfies", "missing", "--unknown", "ignore", "--format", "json");
        Assert.That(missing.ExitCode, Is.EqualTo(2));
        Assert.That((bool?)JObject.Parse(missing.Output)["completed"], Is.False);
    }
    [Test]
    public async Task Data_package_native_field_sets_and_order_are_not_silently_opened()
    {
        using var workspace = new CliWorkspace();
        static string Document(string fields, string? mode = null) => "{\"name\":\"sales\",\"resources\":[{\"name\":\"orders\",\"path\":\"unread.csv\",\"schema\":{" +
            (mode is null ? "" : "\"fieldsMatch\":\"" + mode + "\",") + "\"fields\":[" + fields + "]}}]}";
        const string id = "{\"name\":\"id\",\"type\":\"integer\"}";
        const string extra = "{\"name\":\"extra\",\"type\":\"integer\"}";
        var exact = workspace.Write("exact", Document(id));
        var larger = workspace.Write("larger", Document(id + "," + extra));
        var ordered = workspace.Write("ordered", Document(extra + "," + id));
        var subset = workspace.Write("subset", Document(id, "subset"));
        Assert.That((await CliWorkspace.Run("contract", "satisfies", larger, "--consumer", exact)).ExitCode, Is.EqualTo(1));
        Assert.That((await CliWorkspace.Run("contract", "satisfies", ordered, "--consumer", larger)).ExitCode, Is.EqualTo(1));
        Assert.That((await CliWorkspace.Run("contract", "satisfies", larger, "--consumer", subset)).ExitCode, Is.Zero);
        var partial = workspace.Write("partial", Document(id, "partial"));
        var unknown = await CliWorkspace.Run("contract", "satisfies", partial, "--consumer", partial, "--format", "json");
        Assert.That(unknown.ExitCode, Is.EqualTo(1));
        Assert.That((bool?)JObject.Parse(unknown.Output)["isSatisfied"], Is.False);
    }
}