using System.Text.Json.Nodes;
using NUnit.Framework;
using Packata.Documents.Validation;

namespace Packata.Cli.Testing;

public sealed class CategoryProfileTests
{
    private static string Example => $$$$"""
        {"$schema":"{{{{DescriptorProfiles.CategoryTablesUri}}}}","resources":[
          {"name":"members","type":"table","path":"missing-members.csv","schema":{"fields":[
            {"name":"nationality","type":"string","categories":{"resource":"countries","encodedAs":"value"}},
            {"name":"category","type":"integer","categories":[{"value":1,"label":"Standard"}]}]}},
          {"name":"countries","type":"table","path":"missing-countries.csv",
           "categoryFieldMap":{"value":"code","label":"name","ordered":false},"schema":{"fields":[
             {"name":"code","type":"string","constraints":{"required":true,"unique":true}},
             {"name":"name","type":"string","constraints":{"unique":true}}]}}]}
        """;

    [TestCase(ValidationLayers.Schema)]
    [TestCase(ValidationLayers.Semantic)]
    [TestCase(DescriptorValidation.DefaultLayers)]
    public void ExplicitProfile_UsesOfflineRecipeValidation(ValidationLayers layers)
    {
        var report = DescriptorValidation.Validate(Example, layers: layers);
        Assert.That(report.Valid, Is.True, string.Join(";", report.Diagnostics));
        Assert.That(report.Profile!.Uri, Is.EqualTo(DescriptorProfiles.CategoryTablesUri));
        Assert.That(report.Layers.All(layer => layer.Executed), Is.True);
    }

    [Test]
    public void OfficialProfile_RemainsStrict()
    {
        var root = JsonNode.Parse(Example)!;
        root["$schema"] = DescriptorProfiles.DataPackageUri;
        var report = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(report.Valid, Is.False);
        Assert.That(report.Layers[1].State, Is.EqualTo(ValidationState.Skipped));
        root["resources"]![0]!["schema"]!["fields"]![0]!.AsObject().Remove("categories");
        Assert.That(DescriptorValidation.Validate(root.ToJsonString()).Valid, Is.True);
    }

    [TestCase("{}")]
    [TestCase("{\"resource\":2}")]
    [TestCase("{\"resource\":\"countries\",\"encodedAs\":\"wrong\"}")]
    [TestCase("{\"resource\":\"countries\",\"package\":false}")]
    public void MalformedReferences_FailSchemaAndBlockSemantics(string reference)
    {
        var root = JsonNode.Parse(Example)!;
        root["resources"]![0]!["schema"]!["fields"]![0]!["categories"] = JsonNode.Parse(reference);
        var report = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(report.Layers[0].State, Is.EqualTo(ValidationState.Failed));
        Assert.That(report.Layers[1].State, Is.EqualTo(ValidationState.Skipped));
    }

    [TestCase("{}")]
    [TestCase("{\"value\":1}")]
    [TestCase("{\"value\":\"code\",\"ordered\":\"false\"}")]
    public void MalformedMaps_FailSchema(string map)
    {
        var root = JsonNode.Parse(Example)!;
        root["resources"]![1]!["categoryFieldMap"] = JsonNode.Parse(map);
        Assert.That(DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Schema).Valid, Is.False);
    }

    [Test]
    public void SchemaValidMissingBinding_FailsSemanticChecks()
    {
        var root = JsonNode.Parse(Example)!;
        root["resources"]![0]!["schema"]!["fields"]![0]!["categories"]!["resource"] = "absent";
        var report = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(report.Layers[0].State, Is.EqualTo(ValidationState.Passed));
        Assert.That(report.Layers[1].State, Is.EqualTo(ValidationState.Failed));
    }

    [Test]
    public void IncompatibleValueTypes_FailSemanticsButNotSchema()
    {
        var root = JsonNode.Parse(Example)!;
        root["resources"]![0]!["schema"]!["fields"]![0]!["type"] = "integer";
        var report = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(report.Layers[0].State, Is.EqualTo(ValidationState.Passed));
        Assert.That(report.Layers[1].State, Is.EqualTo(ValidationState.Failed));
    }

    [Test]
    public void Yaml_UsesTheSelectedProfileWithoutFetchingExternalPackages()
    {
        var yaml = $"$schema: {DescriptorProfiles.CategoryTablesUri}\nresources:\n  - name: members\n    path: absent.csv\n    schema:\n      fields:\n        - name: code\n          type: string\n          categories:\n            resource: codes\n            package: https://example.invalid/package.json\n";
        Assert.That(DescriptorValidation.Validate(yaml).Valid, Is.True);
    }

    [Test]
    public async Task Cli_ReportsProfileAsync()
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("package.json", Example);
        var result = await CliWorkspace.Run("contract", "validate", path, "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(JsonNode.Parse(result.Output)!["profile"]!["uri"]!.GetValue<string>(), Is.EqualTo(DescriptorProfiles.CategoryTablesUri));
    }

    [Test]
    public void EmbeddedProfile_HasPublishedIdentityAndNoExternalDependencies()
    {
        using var stream = typeof(DescriptorValidation).Assembly.GetManifestResourceStream("Packata.Documents.Validation.Schemas.category-tables-v1.0.0.json")!;
        Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)),
            Is.EqualTo("BA06A502CFFE98380BEBFE64888235BE6719EDBC0777C135805C318CE53F9469"));
        stream.Position = 0;
        var schema = JsonNode.Parse(stream)!;
        Assert.That(schema["$id"]!.GetValue<string>(), Is.EqualTo(DescriptorProfiles.CategoryTablesUri));
        Assert.That(schema.ToJsonString(), Does.Not.Contain("\"$ref\":\"http"));
    }
}

