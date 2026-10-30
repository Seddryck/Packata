using System.Text.Json.Nodes;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Documents.Validation;

namespace Packata.Cli.Testing;

public sealed class DescriptorValidationTests
{
    private const string Minimal = """
        {"apiVersion":"v3.2.0","kind":"DataContract","id":"contract","version":"1.0.0","status":"draft",
         "schema":[{"name":"users","id":"users-id","properties":[{"name":"id","id":"key-id","logicalType":"integer"}]}]}
        """;

    [TestCase(ValidationLayers.Schema)]
    [TestCase(ValidationLayers.Semantic)]
    [TestCase(DescriptorValidation.DefaultLayers)]
    public void PublicApi_ExecutesExactlySelectedLayers(ValidationLayers selected)
    {
        var result = DescriptorValidation.Validate(Minimal, layers: selected);
        Assert.That(result.Valid, Is.True, string.Join("; ", result.Diagnostics));
        Assert.That(result.Layers.All(l => selected.HasFlag(l.Layer) && l.Executed), Is.True);
        Assert.That(result.Layers.Count, Is.EqualTo(selected == DescriptorValidation.DefaultLayers ? 2 : 1));
    }

    [TestCase("apiVersion", "v3.1.0")]
    [TestCase("apiVersion", "v99.0.0")]
    [TestCase("apiVersion", null)]
    public void UnsupportedOrAbsentVersion_CannotPassEitherLayer(string property, string? value)
    {
        var root = JsonNode.Parse(Minimal)!.AsObject();
        if (value is null) root.Remove(property); else root[property] = value;
        foreach (var selected in new[] { ValidationLayers.Schema, ValidationLayers.Semantic, DescriptorValidation.DefaultLayers })
        {
            var result = DescriptorValidation.Validate(root.ToJsonString(), layers: selected);
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Diagnostics.Any(f => f.Code == "VAL_VERSION"), Is.True);
            Assert.That(result.Layers.All(l => !l.Executed), Is.True);
        }
    }

    [TestCase("[]")]
    [TestCase("{broken")]
    [TestCase("schema: []\nschema: []")]
    [TestCase("a: &a [*a]")]
    [TestCase("---\na: b\n---\nc: d")]
    public void ParsingPrerequisites_AreReportedForSemanticOnly(string text)
    {
        var result = DescriptorValidation.Validate(text, layers: ValidationLayers.Semantic);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Single().Layer, Is.EqualTo(ValidationLayers.Semantic));
        Assert.That(result.Layers.Single().State, Is.EqualTo(ValidationState.Skipped));
    }

    [TestCase("kind: DataContract\napiVersion: v3.2.0\nid: c\nversion: 1.0.0\nextra: secret-value")]
    [TestCase("kind: DataContract\napiVersion: v3.2.0\nid: c\nversion: 1")]
    [TestCase("kind: DataContract\napiVersion: v3.2.0\nid: c\nversion: 1.0.0\ncontractCreatedTs: yesterday")]
    [TestCase("{\"apiVersion\":\"v3.2.0\",\"id\":\"c\",\"version\":\"1.0.0\"}")]
    public void SourceSchema_IsAppliedBeforeNativeDefaultsOrIgnoredFields(string text)
    {
        var result = DescriptorValidation.Validate(text, "odcs");
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Layers[0].State, Is.EqualTo(ValidationState.Failed));
        Assert.That(result.Layers[1].State, Is.EqualTo(ValidationState.Skipped));
        Assert.That(result.Layers[1].Reason, Does.Contain("Schema validation failed"));
        Assert.That(result.Diagnostics.All(d => d.Layer == ValidationLayers.Schema), Is.True);
        Assert.That(string.Join(";", result.Diagnostics), Does.Not.Contain("secret-value"));
    }

    [Test]
    public void MissingSchema_IsSchemaValidButSemanticInvalid()
    {
        var root = JsonNode.Parse(Minimal)!.AsObject(); root.Remove("schema");
        Assert.That(DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Schema).Valid, Is.True);
        Assert.That(DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic).Diagnostics.Any(d => d.Code == "SEM_SCHEMA"), Is.True);
    }

    [TestCase("{\"logicalType\":\"vector\"}", "SEM_VECTOR")]
    [TestCase("{\"logicalType\":\"number\",\"logicalTypeOptions\":{\"precision\":2,\"scale\":3}}", "SEM_DECIMAL")]
    [TestCase("{\"logicalType\":\"number\",\"logicalTypeOptions\":{\"scale\":1}}", "SEM_DECIMAL")]
    [TestCase("{\"logicalType\":\"integer\",\"logicalTypeOptions\":{\"minimum\":10,\"maximum\":2}}", "SEM_BOUNDS")]
    [TestCase("{\"logicalType\":\"integer\",\"logicalTypeOptions\":{\"exclusiveMinimum\":2,\"maximum\":2}}", "SEM_BOUNDS")]
    [TestCase("{\"logicalType\":\"string\",\"logicalTypeOptions\":{\"minLength\":5,\"maxLength\":2}}", "SEM_BOUNDS")]
    [TestCase("{\"logicalType\":\"string\",\"enum\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"a\",\"label\":\"B\"}]}", "SEM_ENUM")]
    [TestCase("{\"logicalType\":\"integer\",\"enum\":[{\"value\":\"1\"}]}", "SEM_ENUM")]
    [TestCase("{\"logicalType\":\"integer\",\"enum\":[{\"value\":null}]}", "SEM_ENUM")]
    [TestCase("{\"primaryKeyPosition\":1}", "SEM_POSITION")]
    [TestCase("{\"logicalType\":\"array\"}", "SEM_ARRAY")]
    [TestCase("{\"logicalType\":\"map\"}", "SEM_MAP")]
    public void PropertySemantics_RejectInvalidDeclarations(string declaration, string code)
    {
        var root = JsonNode.Parse(Minimal)!;
        var property = root["schema"]![0]!["properties"]![0]!.AsObject();
        foreach (var pair in JsonNode.Parse(declaration)!.AsObject()) property[pair.Key] = pair.Value?.DeepClone();
        var result = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == code && d.SourcePath.StartsWith("$/schema/0/properties/0", StringComparison.Ordinal)), Is.True, string.Join(";", result.Diagnostics));
    }

    [TestCase("{}")]
    [TestCase("{\"logicalType\":\"number\",\"logicalTypeOptions\":{\"precision\":5,\"scale\":2,\"exclusiveMinimum\":0,\"maximum\":100}}")]
    [TestCase("{\"logicalType\":\"array\",\"items\":{\"logicalType\":\"object\",\"properties\":[{\"name\":\"x\",\"logicalType\":\"string\"}]}}")]
    public void ValidOptionalAndComplexTypes_PassSemanticChecks(string declaration)
    {
        var root = JsonNode.Parse(Minimal)!;
        var property = JsonNode.Parse(declaration)!.AsObject(); property["name"] = "value";
        root["schema"]![0]!["properties"] = new JsonArray(property);
        var result = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic);
        Assert.That(result.Valid, Is.True, string.Join(";", result.Diagnostics));
    }

    [TestCase("users.id", true)]
    [TestCase("schema/users-id/properties/key-id", true)]
    [TestCase("users.missing", false)]
    [TestCase("other.yaml#schema/users-id/properties/key-id", false)]
    public void References_ResolveNamesAndScopedIdsWithoutNetwork(string target, bool valid)
    {
        var root = JsonNode.Parse(Minimal)!;
        root["schema"]![0]!["properties"]![0]!["relationships"] = new JsonArray(new JsonObject { ["to"] = target });
        var result = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic);
        Assert.That(result.Valid, Is.EqualTo(valid), string.Join(";", result.Diagnostics));
    }

    [Test]
    public void CompositeReferenceLengthAndTypes_AreChecked()
    {
        var root = JsonNode.Parse(Minimal)!;
        root["schema"]![0]!["properties"]!.AsArray().Add(JsonNode.Parse("{\"name\":\"text\",\"logicalType\":\"string\"}"));
        root["schema"]![0]!["relationships"] = JsonNode.Parse("[{\"from\":[\"users.id\"],\"to\":[\"users.text\",\"users.id\"]}]");
        var result = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic);
        Assert.That(result.Diagnostics.Any(d => d.Code == "SEM_RELATIONSHIP"), Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Code == "SEM_REFERENCE_TYPE"), Is.True);
    }

    [TestCase("{\"servers\":[{\"server\":\"a\",\"type\":\"custom\"},{\"server\":\"a\",\"type\":\"custom\"}]}", "SEM_NAME")]
    [TestCase("{\"roles\":[{\"role\":\"reader\",\"id\":\"same\"},{\"role\":\"writer\",\"id\":\"same\"}]}", "SEM_ID")]
    [TestCase("{\"team\":{\"members\":[{\"username\":\"a\",\"dateIn\":\"2026-10-30\",\"dateOut\":\"2025-10-30\"}]}}", "SEM_GOVERNANCE")]
    [TestCase("{\"slaProperties\":[{\"property\":\"availability\",\"value\":101,\"unit\":\"percent\"}]}", "ODCS005")]
    public void RootDeclarationSemantics_AreChecked(string declaration, string code)
    {
        var root = JsonNode.Parse(Minimal)!.AsObject();
        foreach (var pair in JsonNode.Parse(declaration)!.AsObject()) root[pair.Key] = pair.Value?.DeepClone();
        var result = DescriptorValidation.Validate(root.ToJsonString(), layers: ValidationLayers.Semantic);
        Assert.That(result.Diagnostics.Any(d => d.Code == code && d.Severity == MappingSeverity.Error), Is.True, string.Join(";", result.Diagnostics));
    }

    [Test]
    public void QualityRanges_AreValidatedWithoutExecutingSql()
    {
        var root = JsonNode.Parse(Minimal)!;
        root["schema"]![0]!["quality"] = JsonNode.Parse("[{\"type\":\"sql\",\"query\":\"DO NOT EXECUTE\",\"mustBeBetween\":[10,2]}]");
        var result = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(result.Layers[0].State, Is.EqualTo(ValidationState.Passed));
        Assert.That(result.Diagnostics.Any(d => d.Code == "SEM_QUALITY"), Is.True);
    }

    [Test]
    public void ExplicitFormat_DisambiguatesButDoesNotSupplyRequiredSourceFields()
    {
        var root = JsonNode.Parse(Minimal)!.AsObject(); root["resources"] = new JsonArray();
        Assert.That(DescriptorValidation.Validate(root.ToJsonString()).Diagnostics.Any(d => d.Code == "VAL_VERSION"), Is.True);
        Assert.That(DescriptorValidation.Validate(root.ToJsonString(), "odcs", ValidationLayers.Semantic).Diagnostics.Any(d => d.Code == "VAL_VERSION"), Is.False);
    }

    [TestCase("odcs-v3.2.0.json", "EDB41F33EC46E84780E99872AB2BD67F074959D2BF3E9C9FC54E61F8982B0D93")]
    [TestCase("datapackage.json", "A9EF0FC168B3402AE7AA7D22BBCB798E0DB6B639E7EE15FF4AA177463CEA7112")]
    public void OfficialArtifacts_ArePinnedVerbatim(string name, string expectedHash)
    {
        using var stream = typeof(DescriptorValidation).Assembly.GetManifestResourceStream("Packata.Documents.Validation.Schemas." + name)!;
        Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), Is.EqualTo(expectedHash));
    }

    [TestCase("{\"logicalType\":\"vector\"}", true, false)]
    [TestCase("{\"logicalType\":\"string\",\"enum\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"a\",\"label\":\"B\"}]}", true, false)]
    [TestCase("{\"logicalType\":\"integer\",\"enum\":[{\"value\":null}]}", true, false)]
    [TestCase("{\"logicalType\":\"integer\",\"enum\":[{\"value\":\"1\"}]}", true, false)]
    [TestCase("{}", true, true)]
    [TestCase("{\"logicalType\":\"array\",\"items\":{\"logicalType\":\"object\",\"properties\":[{\"name\":\"x\"}]}}", true, true)]
    [TestCase("{\"logicalType\":\"decimal\"}", false, true)]
    [TestCase("{\"logicalType\":\"string\",\"nullable\":true}", false, true)]
    public void SpecificationSchemaDifferences_HaveExplicitLayerBehavior(string declaration, bool schemaValid, bool semanticValid)
    {
        var root = JsonNode.Parse(Minimal)!;
        var property = JsonNode.Parse(declaration)!.AsObject(); property["name"] = "value";
        root["schema"]![0]!["properties"] = new JsonArray(property);
        var text = root.ToJsonString();
        var schema = DescriptorValidation.Validate(text, layers: ValidationLayers.Schema);
        var semantic = DescriptorValidation.Validate(text, layers: ValidationLayers.Semantic);
        Assert.That(schema.Valid, Is.EqualTo(schemaValid), string.Join(";", schema.Diagnostics));
        Assert.That(semantic.Valid, Is.EqualTo(semanticValid), string.Join(";", semantic.Diagnostics));
        Assert.That(DescriptorValidation.Validate(text).Valid, Is.EqualTo(schemaValid && semanticValid));
    }

    [TestCase("01", false)]
    [TestCase("+1", false)]
    [TestCase("0xFF", false)]
    [TestCase("!!int '1'", false)]
    [TestCase("null", false)]
    [TestCase("'01'", true)]
    [TestCase("!!str 01", true)]
    public void YamlScalarKinds_ArePreservedBeforeSchemaValidation(string value, bool valid)
    {
        var text = $"apiVersion: v3.2.0\nkind: DataContract\nversion: 1.0.0\nid: {value}\n";
        var report = DescriptorValidation.Validate(text, layers: ValidationLayers.Schema);
        Assert.That(report.Valid, Is.EqualTo(valid), string.Join(";", report.Diagnostics));
    }

    [TestCase("https://datapackage.org/profiles/1.0/datapackage.json", false)]
    [TestCase("https://example.invalid/custom-profile.json", false)]
    [TestCase("https://datapackage.org/profiles/2.0/datapackage.json", true)]
    public void PackageVersionSelection_IsExplicitAndOffline(string profile, bool valid)
    {
        var root = JsonNode.Parse(CliWorkspace.PackageJson)!; root["$schema"] = profile;
        Assert.That(DescriptorValidation.Validate(root.ToJsonString()).Valid, Is.EqualTo(valid));
    }

    [Test]
    public void UnknownVendorPayloads_AreOpaqueToOdcsPlacementAndIdChecks()
    {
        var root = JsonNode.Parse(Minimal)!;
        root["customProperties"] = JsonNode.Parse("[{\"property\":\"opaque\",\"vendor\":\"another-vendor\",\"value\":{\"encoding\":\"vendor-code\",\"synonyms\":[{\"id\":\"not.an.odcs.id\"}]}}]");
        var result = DescriptorValidation.Validate(root.ToJsonString());
        Assert.That(result.Valid, Is.True, string.Join(";", result.Diagnostics));
    }
}
