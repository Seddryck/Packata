using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class DataValidateCommandTests
{
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: orders
        version: 1.0.0
        status: active
        servers:
          - server: local
            type: local
            format: csv
            path: orders.csv
            customProperties:
              - vendor: packata
                property: lineTerminator
                value: "\n"
        schema:
          - name: orders
            physicalType: table
            properties:
              - name: id
                logicalType: integer
              - name: name
                logicalType: string
        """;

    [TestCase("id,name\nnot-an-integer,Alice\n", 0)]
    [TestCase("id,name\n", 0)]
    [TestCase("id,name\n1\n", 0)]
    [TestCase("name,id\nAlice,1\n", 1)]
    [TestCase("id,name,note\n1,Alice,x\n", 1)]
    [TestCase("id\n1\n", 0)]
    public async Task Csv_structure_uses_raw_headers_and_does_not_execute_value_checks(string csv, int expectedExit)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Odcs);
        workspace.Write("orders.csv", csv);
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(expectedExit), result.Error + result.Output);
        var report = JObject.Parse(result.Output);
        Assert.That((bool)report["isComplete"]!, Is.True);
        Assert.That((bool)report["passed"]!, Is.EqualTo(expectedExit == 0));
        Assert.That(report["results"]!.All(r => (string?)r["descriptor"]!["category"] == "Constraint"), Is.True);
        Assert.That(File.ReadAllText(source), Is.EqualTo(Odcs));
        Assert.That(File.ReadAllText(Path.Combine(workspace.Path, "orders.csv")), Is.EqualTo(csv));
        using var exclusive = File.Open(Path.Combine(workspace.Path, "orders.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase(false, false, "name,id\nAlice,1\n", 0)]
    [TestCase(true, true, "id,x,name\n1,x,Alice\n", 0)]
    [TestCase(false, true, "name\nAlice\n", 0)]
    [TestCase(true, false, "other\nx\n", 0)]
    [TestCase(false, false, "other\nx\n", 1)]
    [TestCase(true, true, "name,x,id\nAlice,x,1\n", 1)]
    public async Task Odcs_custom_settings_control_structural_checks(bool additional, bool order, string csv, int expected)
    {
        using var workspace = new CliWorkspace();
        var declaration = $"    customProperties:\n      - vendor: packata\n        property: allowAdditionalProperties\n        value: {additional.ToString().ToLowerInvariant()}\n      - vendor: packata\n        property: enforcePropertyOrder\n        value: {order.ToString().ToLowerInvariant()}\n";
        var source = workspace.Write("contract.yaml", Odcs.Replace("    properties:", declaration + "    properties:"));
        workspace.Write("orders.csv", csv);
        var result = await CliWorkspace.Run("data", "validate", source, "--asset", "orders", "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(expected), result.Error + result.Output);
    }

    [Test]
    public async Task Required_header_presence_is_independent_of_optional_columns_and_values()
    {
        using var workspace = new CliWorkspace();
        var descriptor = Odcs.Replace("        logicalType: integer", "        logicalType: integer\n        required: true");
        var source = workspace.Write("contract.yaml", descriptor);
        workspace.Write("orders.csv", "name\nAlice\n");
        var missing = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(missing.ExitCode, Is.EqualTo(1), missing.Error + missing.Output);
        Assert.That(missing.Output, Does.Contain("missingRequired"));
        workspace.Write("orders.csv", "id\nnot-an-integer\n");
        var present = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(present.ExitCode, Is.Zero, present.Error + present.Output);
    }

    [TestCase("true", 0)]
    [TestCase("false", 1)]
    [TestCase("\"true\"", 1)]
    [TestCase("\"false\"", 1)]
    [TestCase("1", 1)]
    public async Task Boolean_extensions_preserve_yaml_scalar_kinds(string value, int expected)
    {
        using var workspace = new CliWorkspace();
        var declaration = $"    customProperties:\n      - vendor: packata\n        property: allowAdditionalProperties\n        value: {value}\n";
        var source = workspace.Write("contract.yaml", Odcs.Replace("    properties:", declaration + "    properties:"));
        workspace.Write("orders.csv", "id,name,extra\n1,Alice,x\n");
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(expected), result.Error + result.Output);
        if (value.StartsWith('"') || value == "1")
        {
            Assert.That(result.Error, Does.Contain("ODCS008"));
            Assert.That(result.Output, Is.Empty);
        }
    }

    [TestCase("exact", "name,id\nAlice,1\n", 1)]
    [TestCase("equal", "name,id\nAlice,1\n", 0)]
    [TestCase("subset", "id,name,x\n1,Alice,x\n", 0)]
    [TestCase("superset", "id\n1\n", 0)]
    [TestCase("equal", "id\n1\n", 1)]
    [TestCase("subset", "id\n1\n", 1)]
    [TestCase("partial", "id,x\n1,x\n", 0)]
    [TestCase("partial", "other\nx\n", 1)]
    public async Task DataPackage_native_matching_is_used(string mode, string csv, int expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("package.json", """
            {"name":"sample","resources":[{"name":"orders","path":"orders.csv","format":"csv","dialect":{"header":true,"lineTerminator":"\n"},"schema":{"fieldsMatch":"MODE","fields":[{"name":"id","type":"integer"},{"name":"name","type":"string"}]}}]}
            """.Replace("MODE", mode));
        workspace.Write("orders.csv", csv);
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(expected), result.Error + result.Output);
    }

    [Test]
    public async Task Each_file_is_checked_and_missing_input_is_an_error()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("package.json", """
            {"name":"sample","resources":[{"name":"orders","path":["good.csv","bad.csv"],"format":"csv","dialect":{"header":true,"lineTerminator":"\n"},"schema":{"fields":[{"name":"id","type":"integer"},{"name":"name","type":"string"}]}}]}
            """);
        workspace.Write("good.csv", "id,name\n1,Alice\n");
        workspace.Write("bad.csv", "name,id\nAlice,1\n");
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Error + result.Output);
        var report = JObject.Parse(result.Output);
        Assert.That(report["results"]!.Select(r => (string?)r["descriptor"]!["sourcePath"]).Distinct().Count(), Is.EqualTo(2));
        var missingSource = workspace.Write("missing.yaml", Odcs);
        var missing = await CliWorkspace.Run("data", "validate", missingSource, "--output-format", "json");
        Assert.That(missing.ExitCode, Is.EqualTo(3), missing.Error + missing.Output);
        Assert.That((bool)JObject.Parse(missing.Output)["passed"]!, Is.False);
    }

    [Test]
    public async Task Headerless_input_is_inconclusive_and_never_fabricates_names()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("package.json", """
            {"name":"sample","resources":[{"name":"orders","path":"orders.csv","format":"csv","dialect":{"header":false,"headerRows":[]},"schema":{"fields":[{"name":"id","type":"integer"}]}}]}
            """);
        workspace.Write("orders.csv", "1\n");
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That(result.Output, Does.Contain("HEADER_IDENTITY_UNAVAILABLE"));
    }

    [Test]
    public async Task Duplicate_headers_fail_and_invalid_declarations_stop_before_data_access()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Odcs);
        workspace.Write("orders.csv", "id,name,name\n1,Alice,Alice\n");
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        var report = JObject.Parse(result.Output);
        Assert.That((bool)report["passed"]!, Is.False);
        Assert.That(report["results"]!.Any(r => (string?)r["outcome"] == "Failed"), Is.True);
        var invalid = workspace.Write("invalid.yaml", Odcs.Replace("    properties:",
            "    customProperties:\n      - vendor: packata\n        property: enforcePropertyOrder\n        value: invalid\n    properties:").Replace("orders.csv", "does-not-exist.csv"));
        var rejected = await CliWorkspace.Run("data", "validate", invalid, "--output-format", "json");
        Assert.That(rejected.ExitCode, Is.EqualTo(1), rejected.Error + rejected.Output);
        Assert.That(rejected.Error, Does.Contain("ODCS008"));
        Assert.That(rejected.Output, Is.Empty);
    }

    [Test]
    public async Task Relationship_requires_target_columns_but_does_not_validate_key_values()
    {
        using var workspace = new CliWorkspace();
        var descriptor = """
            {"name":"shop","resources":[
              {"name":"orders","path":"orders.csv","format":"csv","dialect":{"header":true,"lineTerminator":"\n"},
               "schema":{"fields":[{"name":"customer_id","type":"integer"}],"foreignKeys":[{"fields":["customer_id"],"reference":{"resource":"customers","fields":["id"]}}]}},
              {"name":"customers","path":"customers.csv","format":"csv","dialect":{"header":true,"lineTerminator":"\n"},
               "schema":{"fields":[{"name":"id","type":"integer"}]}}
            ]}
            """;
        var source = workspace.Write("package.json", descriptor);
        workspace.Write("orders.csv", "customer_id\n42\n");
        workspace.Write("customers.csv", "id\n1\n");
        var valid = await CliWorkspace.Run("data", "validate", source, "--asset", "orders", "--output-format", "json");
        Assert.That(valid.ExitCode, Is.Zero, valid.Error + valid.Output);
        workspace.Write("customers.csv", "other\n1\n");
        var missingColumn = await CliWorkspace.Run("data", "validate", source, "--asset", "orders", "--output-format", "json");
        Assert.That(missingColumn.ExitCode, Is.EqualTo(1), missingColumn.Error + missingColumn.Output);
        var report = JObject.Parse(missingColumn.Output);
        Assert.That(report["results"]!.Single(r => ((string?)r["descriptor"]!["executionId"])!.Contains("relationship"))["outcome"]!.Value<string>(), Is.EqualTo("Failed"));
    }

    [Test]
    public async Task Empty_file_cannot_pass_by_substituting_declared_schema()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Odcs);
        workspace.Write("orders.csv", "");
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Not.Zero, result.Error + result.Output);
        Assert.That((bool)JObject.Parse(result.Output)["passed"]!, Is.False);
    }

    [Test]
    public async Task External_relationship_does_not_block_local_checks_or_produce_a_complete_pass()
    {
        using var workspace = new CliWorkspace();
        var descriptor = ExternalRelationship();
        var source = workspace.Write("contract.yaml", descriptor);
        workspace.Write("orders.csv", "id,name\n1,Alice\n");
        var result = await CliWorkspace.Run("data", "validate", source, "--asset", "orders", "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(4), result.Error + result.Output);
        Assert.That(result.Error, Does.Contain("SEM_EXTERNAL_REFERENCE Warning"));
        var report = JObject.Parse(result.Output);
        Assert.That(report["results"]!.All(r => (string?)r["outcome"] == "Passed"), Is.True);
        Assert.That(report["results"]!.Count(), Is.EqualTo(2));
        Assert.That((bool)report["isComplete"]!, Is.False);
        Assert.That((bool)report["passed"]!, Is.False);
        Assert.That(report["compilationFindings"]!.Any(f => (string?)f["code"] == "SEM_EXTERNAL_REFERENCE" && (string?)f["kind"] == "Unsupported"), Is.True);
        var offline = Packata.Application.Validation.DescriptorValidation.Validate(descriptor,
            layers: Packata.Application.Validation.ValidationLayers.Semantic);
        Assert.That(offline.Valid, Is.False);
        Assert.That(offline.Diagnostics.Any(f => f.Code == "SEM_EXTERNAL_REFERENCE" && f.Severity == Packata.Core.Contracts.MappingSeverity.Error), Is.True);

        workspace.Write("orders.csv", "name,id\nAlice,1\n");
        var reordered = await CliWorkspace.Run("data", "validate", source, "--asset", "orders", "--output-format", "json");
        var reorderedReport = JObject.Parse(reordered.Output);
        Assert.That(reorderedReport["results"]!.Any(r => (string?)r["outcome"] == "Failed"), Is.True);
        Assert.That((bool)reorderedReport["hasViolations"]!, Is.True);
    }

    [Test]
    public async Task External_relationship_does_not_hide_other_invalid_declarations()
    {
        using var workspace = new CliWorkspace();
        var descriptor = ExternalRelationship().Replace("    properties:",
            "    customProperties:\n      - vendor: packata\n        property: enforcePropertyOrder\n        value: invalid\n    properties:");
        var source = workspace.Write("contract.yaml", descriptor);
        // No data file exists: malformed declarations must remain fatal before acquisition.
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Error + result.Output);
        Assert.That(result.Error, Does.Contain("ODCS008"));
        Assert.That(result.Output, Is.Empty);
    }

    private static string ExternalRelationship() => Odcs.Replace("        logicalType: string",
        "        logicalType: string\n        relationships:\n          - type: foreignKey\n            to: countries.odcs.yaml#/schema/countries/properties/iso_code");

    [Test]
    public async Task Parquet_native_schema_uses_existing_metadata_extractor()
    {
        using var workspace = new CliWorkspace();
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "iris.parquet"), Path.Combine(workspace.Path, "iris.parquet"));
        var source = workspace.Write("package.json", """
            {"name":"iris","resources":[{"name":"flowers","path":"iris.parquet","format":"parquet","schema":{"fieldsMatch":"partial","fields":[{"name":"Sepal.Length","type":"string"}]}}]}
            """);
        var result = await CliWorkspace.Run("data", "validate", source, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Error + result.Output);
        Assert.That(result.Output, Does.Contain("schemaType"));
    }
}
