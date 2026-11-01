using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class LakehouseSchemaCommandTests
{
    private const string Source = """{"resources":[{"name":"orders","schema":{"fields":[{"name":"id","type":"integer","constraints":{"required":true}}]}}]}""";

    [TestCase("iceberg-json", null)]
    [TestCase("paimon-json", null)]
    [TestCase("avro", "hudi")]
    [TestCase("spark-json", "delta")]
    public async Task Explicit_export_routes_stdout_files_and_protects_source(string format, string? profile)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Source);
        var arguments = new List<string> { "schema", "export", source, "--output-format", format };
        if (profile is not null) arguments.AddRange(["--profile", profile]);
        var stdout = await CliWorkspace.Run(arguments.ToArray());
        Assert.That(stdout.ExitCode, Is.Zero, stdout.Error);
        Assert.DoesNotThrow(() => JObject.Parse(stdout.Output));
        var output = Path.Combine(workspace.Path, "arbitrary.txt");
        var saved = await CliWorkspace.Run(arguments.Concat(["-o", output]).ToArray());
        Assert.That(saved.ExitCode, Is.Zero, saved.Error);
        Assert.That(File.ReadAllText(output), Is.EqualTo(stdout.Output));
        var again = await CliWorkspace.Run(arguments.Concat(["-o", output]).ToArray());
        Assert.That(again.ExitCode, Is.EqualTo(3));
        var protectedSource = await CliWorkspace.Run(arguments.Concat(["-o", source]).ToArray());
        Assert.That(protectedSource.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Source));
        var multiple = JObject.Parse(Source);
        ((JArray)multiple["resources"]!).Add(JObject.Parse("""{"name":"other","schema":{"fields":[{"name":"id","type":"integer"}]}}"""));
        var ambiguous = workspace.Write("ambiguous.json", multiple.ToString());
        var ambiguousArgs = arguments.ToArray(); ambiguousArgs[2] = ambiguous;
        Assert.That((await CliWorkspace.Run(ambiguousArgs)).ExitCode, Is.Not.Zero);
        Assert.That((await CliWorkspace.Run(ambiguousArgs.Concat(["--asset", "orders"]).ToArray())).ExitCode, Is.Zero);
        Assert.That(Directory.GetFiles(workspace.Path, "*.tmp"), Is.Empty);
    }

    [TestCase("iceberg-json", null, "number")]
    [TestCase("paimon-json", null, "number")]
    [TestCase("avro", "hudi", "time")]
    [TestCase("spark-json", "delta", "time")]
    public async Task Failed_mapping_never_publishes_partial_artifacts(string format, string? profile, string type)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("invalid.json", Source.Replace("integer", type));
        var output = Path.Combine(workspace.Path, "output.json");
        var args = new List<string> { "schema", "export", source, "--output-format", format, "-o", output };
        if (profile is not null) args.AddRange(["--profile", profile]);
        var failed = await CliWorkspace.Run(args.ToArray());
        Assert.That(failed.ExitCode, Is.Not.Zero);
        Assert.That(failed.Output, Is.Empty);
        Assert.That(File.Exists(output), Is.False);
        Assert.That(Directory.GetFiles(workspace.Path, "*.tmp"), Is.Empty);
    }

    [TestCase("--output-format", "iceberg-json", "--language", "json")]
    [TestCase("--output-format", "paimon-json", "--target", "paimon")]
    [TestCase("--output-format", "avro", "--profile", "delta")]
    [TestCase("--output-format", "spark-json", "--profile", "hudi")]
    [TestCase("--output-format", "avro", "--profile", "unknown")]
    [TestCase("--iceberg")]
    [TestCase("--paimon")]
    [TestCase("--hudi")]
    [TestCase("--delta")]
    public async Task Removed_shortcuts_and_incompatible_options_are_rejected(params string[] options)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.json", Source);
        var result = await CliWorkspace.Run(new[] { "schema", "export", source }.Concat(options).ToArray());
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Is.Empty);
    }
}
