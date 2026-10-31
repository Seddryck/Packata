using NUnit.Framework;
namespace Packata.Cli.Testing;
public class DdlCommandTests
{
    [Test]
    public async Task Unsupported_constraints_do_not_replace_existing_output_and_explicit_opt_out_succeeds()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", """
            resources:
              - name: members
                schema:
                  fields:
                    - name: id
                      type: integer
                      constraints: { unique: true }
            """);
        var output = workspace.Write("schema.sql", "existing SQL");
        var failure = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", "cratedb", "-o", output);
        Assert.That(failure.ExitCode, Is.Not.Zero);
        Assert.That(failure.Output, Is.Empty);
        Assert.That(failure.Error, Does.Contain("PROV009 Error").And.Contain("UNIQUE"));
        Assert.That(File.ReadAllText(output), Is.EqualTo("existing SQL"));
        var allowed = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", "cratedb", "--constraints", "none");
        Assert.That(allowed.ExitCode, Is.Zero, allowed.Error);
        Assert.That(allowed.Output, Does.Contain("CREATE TABLE").And.Not.Contain("UNIQUE"));
    }

    [Test]
    public async Task Snowflake_unenforced_keys_are_information_on_stderr()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Descriptor);
        var result = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", "snowflake");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.Contain("PROV010 Information").And.Not.Contain("PROV010 Warning"));
        Assert.That(result.Output, Does.Contain("PRIMARY KEY").And.Not.Contain("PROV010"));
    }

    [Test]
    public async Task Ddl_resolves_external_ODCS_contracts_relative_to_the_source_without_false_primary_key_warnings()
    {
        using var workspace = new CliWorkspace();
        const string members = """
            {"apiVersion":"v3.2.0","kind":"DataContract","id":"membership","schema":[{"name":"members","properties":[{"name":"member_id","logicalType":"integer","primaryKey":true},{"name":"country","logicalType":"string","physicalType":"varchar(2)","relationships":[{"to":"countries.odcs.yaml#/schema/countries/properties/iso_code"}]}]}]}
            """;
        const string countries = """
            {"apiVersion":"v3.2.0","kind":"DataContract","id":"lookup","schema":[{"name":"countries","properties":[{"name":"iso_code","logicalType":"string","physicalType":"varchar(2)","primaryKey":true}]}]}
            """;
        var source = workspace.Write("descriptors/members.odcs.yaml", members);
        var target = workspace.Write("descriptors/countries.odcs.yaml", countries);
        var result = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", "sqlserver");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.Not.Contain("PROV004").And.Not.Contain("PROV003"));
        Assert.That(result.Output, Does.Contain("PRIMARY KEY").And.Contain("REFERENCES [countries] ([iso_code])"));
        Assert.That(result.Output, Does.Not.Contain("CREATE TABLE [countries]"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(members));
        Assert.That(File.ReadAllText(target), Is.EqualTo(countries));
        File.Delete(target);
        var missing = await CliWorkspace.Run("schema", "generate", source, "--target", "ddl", "--dialect", "sqlserver");
        Assert.That(missing.ExitCode, Is.Not.Zero);
        Assert.That(missing.Error, Does.Contain("PROV003").And.Contain("countries.odcs.yaml").And.Not.Contain("Relationship to 'odcs'"));
        Assert.That(missing.Output, Is.Empty);
    }

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
    [TestCase("--target", "ddl", "--dialect", "drill")]
    [TestCase("--target", "ddl", "--dialect", "questdb")]
    [TestCase("--target", "ddl", "--dialect", "sqlite", "--language", "python")]
    [TestCase("--target", "ddl", "--dialect", "sqlite", "--template", "missing.scriban")]
    public async Task Invalid_selectors_fail_before_reading(params string[] options)
    {
        var result = await CliWorkspace.Run(["schema", "generate", "missing.yaml", .. options]);
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Error);
        Assert.That(result.Output, Is.Empty);
    }
}
