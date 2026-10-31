using NUnit.Framework;
namespace Packata.Cli.Testing;
public class DdlCommandTests
{
    private const string Descriptor = """
        resources:
          - name: orders
            schema:
              fields:
                - name: id
                  type: integer
              primaryKey: [id]
          - name: customers
            schema:
              fields:
                - name: name
                  type: string
        """;
    [TestCase("postgresql")]
    [TestCase("sqlserver")]
    [TestCase("sqlite")]
    public async Task Ddl_generates_without_connection_and_protects_files(string dialect)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Descriptor);
        var result = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", dialect);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("orders").And.Contain("customers"));
        var selected = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", dialect, "--language", "sql", "--asset", "orders", "--constraints", "none");
        Assert.That(selected.ExitCode, Is.Zero, selected.Error);
        Assert.That(selected.Output, Does.Not.Contain("customers").And.Not.Contain("PRIMARY KEY"));
        var overwrite = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", dialect, "-o", source);
        Assert.That(overwrite.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Descriptor));
    }
    [TestCase("--target", "ddl")]
    [TestCase("--target", "ddl", "--dialect", "missing")]
    [TestCase("--target", "ddl", "--dialect", "sqlite", "--language", "python")]
    [TestCase("--target", "ddl", "--dialect", "sqlite", "--template", "missing.scriban")]
    public async Task Invalid_selectors_fail_before_reading(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "generate", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }
}
