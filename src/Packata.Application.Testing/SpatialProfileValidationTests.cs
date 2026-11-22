using System.Text.Json.Nodes;
using NUnit.Framework;
using Packata.Application.Validation;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;

namespace Packata.Application.Testing;

public sealed class SpatialProfileValidationTests
{
    private static readonly ContractMappingOptions SpatialOptions = ContractMappingOptions.Default with
    { Odcs = new() { EnableSpatialRfc0048 = true } };

    [TestCase("geometry", "{\"crs\":\"EPSG:4326\",\"subType\":\"Point\",\"encoding\":\"geojson\"}")]
    [TestCase("geography", "{}")]
    [TestCase("geography", "{\"dimensions\":3,\"bbox\":[0,0,0,1,1,1],\"epoch\":2024.5,\"algorithm\":\"karney\",\"orientation\":\"counterclockwise\"}")]
    public void ProposedSpatialProfile_IsExplicitAndDoesNotWeakenStandardSchema(string type, string options)
    {
        var text = Document(type, options);
        var standard = DescriptorValidation.Validate(text, "odcs");
        Assert.That(standard.Valid, Is.False);
        Assert.That(standard.Profile!.Uri, Is.EqualTo(DescriptorProfiles.OdcsUri));
        var extended = Validate(text);
        Assert.That(extended.Valid, Is.True, string.Join("; ", extended.Diagnostics));
        Assert.That(extended.Profile!.Uri, Is.EqualTo(DescriptorProfiles.OdcsSpatialRfc0048Uri));
        Assert.That(DescriptorValidation.Validate(text, "odcs").Valid, Is.False);
        var mapped = (ContractDocument.Parse(text) with { MappingOptions = SpatialOptions }).Map();
        Assert.That(mapped.Diagnostics, Is.Empty);
        Assert.That(mapped.RequireValue().Assets[0].Schema!.Fields[0].Spatial, Is.Not.Null);
    }

    [TestCase("geometry", "{}")]
    [TestCase("geometry", "{\"crs\":\"EPSG:4326\",\"algorithm\":\"karney\"}")]
    [TestCase("geography", "{\"dimensions\":5}")]
    [TestCase("geography", "{\"dimensions\":\"3\"}")]
    [TestCase("geography", "{\"subType\":\"Feature\"}")]
    [TestCase("geography", "{\"encoding\":\"topojson\"}")]
    [TestCase("geography", "{\"bbox\":[0,0,1]}")]
    [TestCase("geography", "{\"dimensions\":4,\"bbox\":[0,0,1,1]}")]
    [TestCase("geography", "{\"epoch\":\"2024.5\"}")]
    [TestCase("geography", "{\"unknownOption\":true}")]
    [TestCase("integer", "{\"encoding\":\"geojson\"}")]
    public void ExtendedSchema_RejectsMalformedSpatialOptionsAndUnrelatedExtensions(string type, string options)
    {
        var result = Validate(Document(type, options));
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Layers[0].State, Is.EqualTo(ValidationState.Failed));
    }

    [Test]
    public void SemanticOnlyValidation_RespectsTheSameOptInAndReportsInvalidOptions()
    {
        var text = Document("geometry", "{}");
        var result = Validate(text, ValidationLayers.Semantic);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == "SPATIAL_OPTIONS"), Is.True);
        var standard = DescriptorValidation.Validate(Document("geography", "{}"), "odcs", ValidationLayers.Semantic);
        Assert.That(standard.Valid, Is.False);
    }

    private static DescriptorValidationReport Validate(string text, ValidationLayers layers = DescriptorValidation.DefaultLayers)
        => DescriptorValidation.Validate(text, "odcs", layers, null, default, SpatialOptions);

    private static string Document(string type, string options)
    {
        var root = JsonNode.Parse("""
            {"apiVersion":"v3.2.0","kind":"DataContract","id":"places","version":"1.0.0","status":"draft",
             "schema":[{"name":"places","properties":[{"name":"location"}]}]}
            """)!;
        var field = root["schema"]![0]!["properties"]![0]!;
        field["logicalType"] = type;
        field["logicalTypeOptions"] = JsonNode.Parse(options);
        return root.ToJsonString();
    }
}
