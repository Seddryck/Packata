using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace Packata.Cli.Testing;
public class StructuredExportCommandTests
{
    private const string Descriptor = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
                logicalTypeOptions:
                  format: i64
                required: true
              - name: amount
                logicalType: number
                logicalTypeOptions:
                  precision: 12
                  scale: 2
        """;
    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task Formats_use_shared_asset_selection_and_safe_output(string format)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Descriptor);
        var result = await CliWorkspace.Run("schema", "export", source, "--output-format", format);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.DoesNotThrow(() => JObject.Parse(result.Output));
        var path = Path.Combine(workspace.Path, "schema.json");
        var first = await CliWorkspace.Run("schema", "export", source, "--output-format", format, "-o", path);
        Assert.That(first.ExitCode, Is.Zero, first.Error);
        Assert.That(File.ReadAllText(path), Is.EqualTo(result.Output));
        var overwrite = await CliWorkspace.Run("schema", "export", source, "--output-format", format, "-o", source);
        Assert.That(overwrite.ExitCode, Is.EqualTo(3));
        var badSource = workspace.Write("invalid.yaml", Descriptor.Replace("logicalType: integer", "logicalType: vector"));
        var failurePath = Path.Combine(workspace.Path, "not-created.json");
        var failed = await CliWorkspace.Run("schema", "export", badSource, "--output-format", format, "-o", failurePath);
        Assert.That(failed.ExitCode, Is.EqualTo(1), failed.Error);
        Assert.That(failed.Output, Is.Empty);
        Assert.That(File.Exists(failurePath), Is.False);
    }
    [TestCase("--output-format", "avro", "--target", "record")]
    [TestCase("--output-format=avro", "--language=csharp")]
    [TestCase("--output-format", "spark-json", "--profile", "delta")]
    [TestCase("--output-format", "avro", "--profile", "hudi")]
    [TestCase("--avro")]
    [TestCase("--spark-json")]
    [TestCase("--output-format", "avro", "--invented", "value")]
    public async Task Unsupported_options_fail_before_source_reads(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "export", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }
}
