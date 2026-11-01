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

    [TestCase("record", "record Orders")]
    [TestCase("datatable", "DataTable Create()")]
    [TestCase("polars", "PolarsSchema Create()")]
    public async Task Builtins_write_only_generated_content_to_stdout(string selector, string expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source);
        var result = await CliWorkspace.Run("schema", "generate", source, "--target", selector, "--language", "csharp");
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
        var result = await CliWorkspace.Run("schema", "generate", source, "--template", "bar.template.cs", "--define", "count=42");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Is.EqualTo("orders 42"));
        var output = Path.Combine(workspace.Path, "Orders.cs");
        var first = await CliWorkspace.Run("schema", "generate", source, "--target", "record", "--language", "csharp", "--output", output);
        var second = await CliWorkspace.Run("schema", "generate", source, "--target", "record", "--language", "csharp", "--output", output);
        var overwrite = await CliWorkspace.Run("schema", "generate", source, "--target", "record", "--language", "csharp", "--output", source);
        Assert.That(first.ExitCode, Is.Zero, first.Error);
        Assert.That(first.Output, Is.Empty);
        Assert.That(second.ExitCode, Is.EqualTo(3));
        Assert.That(overwrite.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Source));
    }

    [TestCase("--target", "arrow")]
    [TestCase("--target", "arrow", "--language", "rust")]
    [TestCase("--target", "arrow", "--language", "python", "--template", "missing.scriban")]
    [TestCase("--target", "arrow", "--language", "python", "--output-format", "json-schema")]
    [TestCase("--arrow")]
    [TestCase("--python")]
    [TestCase("--record")]
    [TestCase("--template", "missing.scriban", "--engine", "absent")]
    [TestCase("--target", "arrow", "--language", "python", "--define", "x=1", "--define", "x=2")]
    public async Task Conflicts_are_rejected_before_source_or_template_reads(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "generate", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }

    [TestCase("--target", "record")]
    [TestCase("--target", "record", "--language", "python")]
    [TestCase("--target", "record", "--language", "csharp", "--template", "missing.scriban")]
    [TestCase("--target", "record", "--language", "csharp", "--output-format", "json-schema")]
    [TestCase("--target", "record", "--language", "csharp", "--profile", "hudi")]
    public async Task Canonical_invalid_selections_fail_before_reading(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "generate", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task Removed_shortcuts_and_top_level_export_explain_migration()
    {
        foreach (var args in new[] { new[] { "export", "missing.yaml" }, new[] { "schema", "export", "missing.yaml", "--record" }, new[] { "schema", "generate", "missing.yaml", "--ddl" }, new[] { "schema", "export", "missing.yaml", "--template", "missing.scriban" } })
        {
            var result = await CliWorkspace.Run(args);
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Error, Does.Contain("schema generate --target").And.Contain("schema export --output-format"));
        }
    }

    [Test]
    public async Task Missing_templates_and_mapping_errors_emit_no_partial_file()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source.Replace("logicalType: integer", "logicalType: unknown"));
        var output = Path.Combine(workspace.Path, "not-created.cs");
        var failed = await CliWorkspace.Run("schema", "generate", source, "--target", "record", "--language", "csharp", "--output", output);
        Assert.That(failed.ExitCode, Is.EqualTo(1));
        Assert.That(failed.Output, Is.Empty);
        Assert.That(File.Exists(output), Is.False);
        var missing = await CliWorkspace.Run("schema", "generate", source, "--template", "missing.scriban");
        Assert.That(missing.ExitCode, Is.EqualTo(3));
    }

    [TestCase("csharp", "Schema Create()")]
    [TestCase("python", "import pyarrow as pa")]
    public async Task Arrow_code_uses_explicit_language_and_protects_output(string language, string expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Source);
        var output = Path.Combine(workspace.Path, "output.txt");
        var result = await CliWorkspace.Run("schema", "generate", source, "--target", "arrow", "--language", language, "--asset", "orders", "-o", output);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(File.ReadAllText(output), Does.Contain(expected));
        var repeat = await CliWorkspace.Run("schema", "generate", source, "--target", "arrow", "--language", language, "-o", output);
        Assert.That(repeat.ExitCode, Is.EqualTo(3));
        var wrongOperation = await CliWorkspace.Run("schema", "export", source, "--target", "arrow", "--language", language);
        Assert.That(wrongOperation.ExitCode, Is.EqualTo(2));
    }
}
