using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class SatisfiesCommandTests
{
    private string directory = null!;
    [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "ccr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TearDown] public void TearDown() => Directory.Delete(directory, true);
    private string Write(string file, string text) { var path = Path.Combine(directory, file); File.WriteAllText(path, text); return path; }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.That(process.WaitForExit(30000), Is.True);
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
    private const string Ccr = """
        apiVersion: v1
        kind: ConsumerContract
        id: report
        target:
          id: sales
        datasets:
          - name: orders
            fields:
              - name: id
                type: string
                nullable: false
                length: 50
              - name: amount
                type: decimal
                precision: 18
                scale: 2
        """;
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: string
                required: true
                logicalTypeOptions:
                  maxLength: 30
              - name: amount
                logicalType: number
                logicalTypeOptions:
                  precision: 16
                  scale: 2
        """;

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Odcs_and_ccr_support_yaml_and_json(bool producerJson, bool consumerJson)
    {
        var producer = producerJson ? ToJson(Odcs) : Odcs;
        var consumer = consumerJson ? Packata.ConsumerContracts.ConsumerContractSerializer.Serialize(
            Packata.ConsumerContracts.ConsumerContractSerializer.Deserialize(Ccr), true) : Ccr;
        var result = Run("contract", "satisfies", Write(producerJson ? "odcs.json" : "odcs.yaml", producer),
            "-r", Write(consumerJson ? "ccr.json" : "ccr.yaml", consumer), "--format", "json");
        Assert.That(result.Code, Is.Zero, result.Error + result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo("Satisfied"));
    }

    private static string ToJson(string yaml)
    {
        var obj = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<object>(yaml);
        return new YamlDotNet.Serialization.SerializerBuilder().JsonCompatible().Build().Serialize(obj);
    }

    [TestCase("logicalType: string", "logicalType: boolean", "Unsatisfied")]
    [TestCase("maxLength: 30", "minLength: 1", "Unknown")]
    [TestCase("name: id", "name: missing", "Unsatisfied")]
    public void Unsatisfied_and_unknown_return_one(string before, string after, string expected)
    {
        var result = Run("contract", "satisfies", Write("odcs.yaml", Odcs.Replace(before, after)),
            "--requirements", Write("ccr.yaml", Ccr), "--format", "json");
        Assert.That(result.Code, Is.EqualTo(1), result.Error + result.Output);
        Assert.That(result.Output, Does.Contain(expected));
    }

    [Test]
    public void Text_distinguishes_unknown_and_quiet_suppresses_output()
    {
        var producer = Write("odcs.yaml", Odcs.Replace("maxLength: 30", "minLength: 1"));
        var ccr = Write("ccr.yaml", Ccr);
        var text = Run("contract", "satisfies", producer, "-r", ccr);
        Assert.That(text.Output, Does.Contain("? id: length cannot be evaluated"));
        var quiet = Run("contract", "satisfies", producer, "-r", ccr, "--quiet");
        Assert.That(quiet.Code, Is.EqualTo(1));
        Assert.That(quiet.Output + quiet.Error, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Data_package_supports_yaml_and_json(bool json)
    {
        const string package = """
            name: sales
            resources:
              - name: orders
                path: orders.csv
                schema:
                  fields:
                    - name: id
                      type: string
                      constraints:
                        required: true
                        maxLength: 30
            """;
        var ccr = "apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets:\n  - name: orders\n    fields:\n      - name: id\n        type: string\n        nullable: false\n        length: 50\n";
        var result = Run("contract", "satisfies", Write(json ? "package.json" : "package.yaml", json ? ToJson(package) : package),
            "-r", Write("ccr.yaml", ccr));
        Assert.That(result.Code, Is.Zero, result.Error + result.Output);
    }

    [TestCase("garbage")]
    [TestCase("apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets: []")]
    [TestCase("apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets:\n  - name: orders\n    fields:\n      - name: id\n        type: boolean\n        length: 50")]
    public void Invalid_ccr_returns_two_and_quiet_is_silent(string invalid)
    {
        var producer = Write("odcs.yaml", Odcs);
        var ccr = Write("ccr.yaml", invalid);
        var result = Run("contract", "satisfies", producer, "-r", ccr, "--quiet");
        Assert.That(result.Code, Is.EqualTo(2));
        Assert.That(result.Output + result.Error, Is.Empty);
    }

    [Test]
    public void Invalid_producer_missing_arguments_and_bad_options_return_two()
    {
        var ccr = Write("ccr.yaml", Ccr);
        Assert.That(Run("contract", "satisfies", Write("bad.yaml", "garbage"), "-r", ccr).Code, Is.EqualTo(2));
        Assert.That(Run("contract", "satisfies").Code, Is.EqualTo(2));
        Assert.That(Run("contract", "satisfies", "missing", "-r", ccr).Code, Is.EqualTo(2));
        Assert.That(Run("contract", "satisfies", "--unknown").Code, Is.EqualTo(2));
        Assert.That(Run("contract", "satisfies", "missing", "-r", ccr, "--format", "xml").Code, Is.EqualTo(2));
    }
}
