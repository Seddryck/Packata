using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using NUnit.Framework;
using Packata.Documents.Validation;

namespace Packata.Cli.Testing;

public sealed class SchemaDiagnosticsTests
{
    private const string Reproduction = """
        {"$schema":"https://datapackage.org/profiles/2.0/datapackage.json","resources":[
          {"name":"members","path":"members.csv","schema":{"fields":[
            {"name":"nationality","type":"string","categories":{"resource":"countries","encodedAs":"value"}}],
            "primaryKey":["nationality"]}}]}
        """;
    private const string CategoryPath = "$/resources/0/schema/fields/0/categories";

    [Test]
    public void OfficialRecipeMismatch_ReportsOnlyTheActualCategoryShape()
    {
        var report = DescriptorValidation.Validate(Reproduction);
        Assert.That(report.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(report.Diagnostics[0].SourcePath, Is.EqualTo(CategoryPath));
        Assert.That(report.Diagnostics[0].Message, Does.Contain("Data Package v2.0").And.Contain("Expected array"));
        Assert.That(report.Layers[1].State, Is.EqualTo(ValidationState.Skipped));
    }

    [TestCase("path", "123", "$/resources/0/path")]
    [TestCase("primaryKey", "123", "$/resources/0/schema/primaryKey")]
    [TestCase("type", "\"secret-unsupported-type\"", "$/resources/0/schema/fields/0")]
    public void IndependentInvalidDeclarations_ArePreserved(string property, string value, string path)
    {
        var root = JsonNode.Parse(Reproduction)!;
        var target = property switch
        {
            "path" => root["resources"]![0]!,
            "primaryKey" => root["resources"]![0]!["schema"]!,
            _ => root["resources"]![0]!["schema"]!["fields"]![0]!
        };
        target[property] = JsonNode.Parse(value);
        var report = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Schema);
        Assert.That(report.Valid, Is.False);
        Assert.That(report.Diagnostics.Any(finding => finding.SourcePath == path), Is.True, string.Join(";", report.Diagnostics));
        Assert.That(string.Join(";", report.Diagnostics), Does.Not.Contain("secret-unsupported-type"));
        if (property != "type") Assert.That(report.Diagnostics.Any(finding => finding.SourcePath == CategoryPath), Is.True);
    }

    [TestCase("\"members.csv\"", "[\"nationality\"]")]
    [TestCase("[\"members.csv\",\"other.csv\"]", "\"nationality\"")]
    public void ValidAlternativeForms_DoNotLeakRejectedBranchErrors(string path, string key)
    {
        var root = JsonNode.Parse(Reproduction)!;
        root["resources"]![0]!["path"] = JsonNode.Parse(path);
        root["resources"]![0]!["schema"]!["primaryKey"] = JsonNode.Parse(key);
        root["resources"]![0]!["schema"]!["fields"]![0]!.AsObject().Remove("categories");
        Assert.That(DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Schema).Diagnostics, Is.Empty);
    }

    [Test]
    public async Task CliJsonAndText_UseTheSharedFindingsAsync()
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("invalid.json", Reproduction);
        var json = await CliWorkspace.Run("contract", "validate", path, "--schema", "--output-format", "json");
        var text = await CliWorkspace.Run("contract", "validate", path, "--schema");
        var expected = DescriptorValidation.Validate(Reproduction, layers: ValidationLayers.Schema).Diagnostics.Single();
        var diagnostics = JsonNode.Parse(json.Output)!["diagnostics"]!.AsArray();
        Assert.That(json.ExitCode, Is.EqualTo(1));
        Assert.That(text.ExitCode, Is.EqualTo(1));
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0]!["message"]!.GetValue<string>(), Is.EqualTo(expected.Message));
        Assert.That(text.Output + text.Error, Does.Contain(expected.Message));
    }

    [TestCase("{\"oneOf\":[{\"type\":\"number\"},{\"type\":\"integer\"}]}", "1", "$", "More than one")]
    [TestCase("{\"anyOf\":[{\"type\":\"array\"},{\"type\":\"string\"}]}", "true", "$", "Expected array or string")]
    [TestCase("{\"if\":{\"type\":\"string\"},\"then\":{\"minLength\":5},\"else\":{\"minimum\":10}}", "1", "$", "minimum")]
    [TestCase("{\"if\":{\"type\":\"string\"},\"then\":{\"minLength\":5},\"else\":{\"minimum\":10}}", "\"x\"", "$", "minLength")]
    [TestCase("{\"allOf\":[{\"properties\":{\"a\":{\"anyOf\":[{\"type\":\"array\"},{\"type\":\"string\"}]}}}]}", "{\"a\":true}", "$/a", "Expected array or string")]
    [TestCase("{\"$defs\":{\"choice\":{\"oneOf\":[{\"properties\":{\"kind\":{\"const\":\"a\"},\"value\":{\"type\":\"string\"}}},{\"properties\":{\"kind\":{\"const\":\"b\"}}}]}},\"$ref\":\"#/$defs/choice\"}", "{\"kind\":\"a\",\"value\":false}", "$/value", "Expected string")]
    public void NestedUnionsConditionsAndAmbiguity_KeepTheirMeaning(string definition, string input, string path, string message)
    {
        var findings = Project(definition, input);
        Assert.That(findings, Has.Count.EqualTo(1), string.Join(";", findings));
        Assert.That(findings[0].SourcePath, Is.EqualTo(path));
        Assert.That(findings[0].Message, Does.Contain(message));
    }

    [Test]
    public void IndependentAssertions_AreNotHiddenByApplicatorSummaries()
    {
        var findings = Project("""{"properties":{"a":{"type":"string"},"b":{"type":"array"}},"required":["c"]}""", """{"a":false,"b":123}""");
        Assert.That(findings.Select(finding => finding.SourcePath), Is.EquivalentTo(new[] { "$/a", "$/b", "$" }));
    }

    [Test]
    public void OdcsConditionalServerRequirements_DoNotReportRejectedServerTypes()
    {
        var source = """{"apiVersion":"v3.2.0","kind":"DataContract","id":"c","version":"1.0.0","servers":[{"server":"s","type":"s3"}]}""";
        var report = DescriptorValidation.Validate(source, layers: ValidationLayers.Schema);
        Assert.That(report.Valid, Is.False);
        Assert.That(report.Diagnostics.All(finding => finding.SourcePath.StartsWith("$/servers/0", StringComparison.Ordinal)), Is.True, string.Join(";", report.Diagnostics));
        Assert.That(string.Join(";", report.Diagnostics), Does.Not.Contain("'if'").And.Not.Contain("/type"));
    }

    private static IReadOnlyList<ValidationFinding> Project(string definition, string input)
    {
        var schema = JsonSchema.FromText(definition, new BuildOptions { Dialect = Dialect.Draft201909 });
        var instance = JsonSerializer.Deserialize<JsonElement>(input);
        var result = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        return SchemaDiagnostics.Project(schema, result, instance, new("https://example.invalid/test", "Test profile", "test"));
    }
}
