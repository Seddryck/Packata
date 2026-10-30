using NUnit.Framework;

namespace Packata.Cli.Testing;

public class SchemaGenerationCommandTests
{
    private const string Source = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
                required: true
        """;

    [TestCase("--record", "record Orders")]
    [TestCase("--datatable", "DataTable Create()")]
    [TestCase("--polars", "PolarsSchema Create()")]
    public async Task Builtins_write_only_generated_content_to_stdout(string selector, string expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source);
        var result = await CliWorkspace.Run("schema", "export", source, selector);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain(expected));
        Assert.That(result.Error, Does.Not.Contain("Error"));
    }

    [Test]
    public async Task Custom_templates_resolve_from_descriptor_and_files_are_protected()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("nested/contract.yaml", Source);
        workspace.Write("nested/bar.template.cs", "{{ model.asset.name }} {{ model.variables.count }}");
        var result = await CliWorkspace.Run("schema", "export", source, "--template", "bar.template.cs", "--define", "count=42");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Is.EqualTo("orders 42"));
        var output = Path.Combine(workspace.Path, "Orders.cs");
        var first = await CliWorkspace.Run("schema", "export", source, "--record", "--output", output);
        var second = await CliWorkspace.Run("schema", "export", source, "--record", "--output", output);
        var overwrite = await CliWorkspace.Run("schema", "export", source, "--record", "--output", source);
        Assert.That(first.ExitCode, Is.Zero, first.Error);
        Assert.That(first.Output, Is.Empty);
        Assert.That(second.ExitCode, Is.EqualTo(3));
        Assert.That(overwrite.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Source));
    }

    [TestCase("--record", "--polars")]
    [TestCase("--record", "--template", "missing.scriban")]
    [TestCase("--record", "--output-format", "json-schema")]
    [TestCase("--template", "missing.scriban", "--engine", "absent")]
    [TestCase("--record", "--define", "x=1", "--define", "x=2")]
    public async Task Conflicts_are_rejected_before_source_or_template_reads(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "export", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task Missing_templates_and_mapping_errors_emit_no_partial_file()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source.Replace("logicalType: integer", "logicalType: unknown"));
        var output = Path.Combine(workspace.Path, "not-created.cs");
        var failed = await CliWorkspace.Run("schema", "export", source, "--record", "--output", output);
        Assert.That(failed.ExitCode, Is.EqualTo(1));
        Assert.That(failed.Output, Is.Empty);
        Assert.That(File.Exists(output), Is.False);
        var missing = await CliWorkspace.Run("schema", "export", source, "--template", "missing.scriban");
        Assert.That(missing.ExitCode, Is.EqualTo(3));
    }
}
