using System.Text;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Mapping;

public sealed class SpatialMappingTests
{
    private static readonly OdcsMappingOptions SpatialOptions = new() { EnableSpatialRfc0048 = true };

    [TestCase("geometry", SpatialModel.Geometry)]
    [TestCase("geography", SpatialModel.Geography)]
    public void Map_SameCoordinates_PreservesDeclaredSpatialModel(string type, SpatialModel expected)
    {
        var native = Load(Field(type, "{subType: Point, crs: 'EPSG:4326', dimensions: 2, encoding: geojson}"));
        var result = native.ToCanonicalContract(SpatialOptions);
        var field = result.RequireValue().Assets.Single().Schema!.Fields.Single();
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(field.Spatial!.Model, Is.EqualTo(expected));
        Assert.That(field.Spatial.SubType, Is.EqualTo(SpatialSubtype.Point));
        Assert.That(field.Spatial.Crs, Is.EqualTo("EPSG:4326"));
        Assert.That(FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(System.Text.Json.JsonElement)));
        Assert.That(native.Schema[0].Properties[0].Examples!.Single(), Does.Contain("[4.35,50.85]"));
    }

    [TestCase(2, "[-180, -90, 180, 90]")]
    [TestCase(3, "[-180, -90, 0, 180, 90, 100]")]
    [TestCase(4, "[-180, -90, 0, 0, 180, 90, 100, 10]")]
    public void NativeRoundTrip_PreservesSpatialOptionsAndScalarKinds(int dimensions, string bbox)
    {
        var yaml = Field("geography", $"{{subType: Point, crs: 'OGC:CRS84', dimensions: {dimensions}, encoding: ewkb, algorithm: karney, bbox: {bbox}, orientation: counterclockwise, epoch: 2024.5}}");
        var first = Load(yaml);
        var second = Load(new DataContractSerializer().Serialize(first));
        foreach (var native in new[] { first, second })
        {
            var result = native.ToCanonicalContract(SpatialOptions);
            Assert.That(result.Diagnostics, Is.Empty);
            var field = result.RequireValue().Assets[0].Schema!.Fields[0];
            var spatial = field.Spatial!;
            Assert.That(spatial.Model, Is.EqualTo(SpatialModel.Geography));
            Assert.That(spatial.Dimensions, Is.EqualTo(dimensions));
            Assert.That(spatial.Crs, Is.EqualTo("OGC:CRS84"));
            Assert.That(spatial.Encoding, Is.EqualTo(SpatialEncoding.Ewkb));
            Assert.That(spatial.Algorithm, Is.EqualTo("karney"));
            Assert.That(spatial.Orientation, Is.EqualTo("counterclockwise"));
            Assert.That(spatial.Epoch, Is.EqualTo(2024.5));
            Assert.That(spatial.BoundingBox, Has.Count.EqualTo(dimensions * 2));
            Assert.That(spatial.BoundingBox!.First(), Is.EqualTo(-180));
            Assert.That(field.Extensions["odcs"]["logicalTypeOptions"], Is.Not.Null);
        }
    }

    [TestCase("Point", SpatialSubtype.Point)]
    [TestCase("LineString", SpatialSubtype.LineString)]
    [TestCase("Polygon", SpatialSubtype.Polygon)]
    [TestCase("MultiPoint", SpatialSubtype.MultiPoint)]
    [TestCase("MultiLineString", SpatialSubtype.MultiLineString)]
    [TestCase("MultiPolygon", SpatialSubtype.MultiPolygon)]
    [TestCase("GeometryCollection", SpatialSubtype.GeometryCollection)]
    public void Map_SpatialSubtype_IsIndependentOfDefaultClrRepresentation(string subtype, SpatialSubtype expected)
    {
        var result = Load(Field("geometry", $"{{subType: {subtype}, crs: 'EPSG:3857', encoding: wkt}}"))
            .ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics, Is.Empty);
        var field = result.RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.Spatial!.SubType, Is.EqualTo(expected));
        Assert.That(FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict).RuntimeType, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void Map_ProposedTypes_RequireOptInAndPreserveAbsentDeclarations()
    {
        var native = Load(Field("geography", "{}"));
        var standard = native.ToCanonicalContract();
        Assert.That(standard.Diagnostics.Single().Code, Is.EqualTo("SPATIAL_PROFILE"));
        Assert.That(standard.Value!.Assets[0].Schema!.Fields[0].Spatial, Is.Null);
        var field = native.ToCanonicalContract(SpatialOptions).RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.Spatial!.Crs, Is.Null);
        Assert.That(field.Spatial.Dimensions, Is.Null);
        Assert.That(field.Spatial.Encoding, Is.Null);
        Assert.That(FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict).RuntimeType, Is.Null);
    }

    [TestCase("geometry", "{}")]
    [TestCase("geometry", "{crs: 'EPSG:4326', algorithm: karney}")]
    [TestCase("geography", "{dimensions: 5}")]
    [TestCase("geography", "{dimensions: two}")]
    [TestCase("geography", "{dimensions: '3'}")]
    [TestCase("geography", "{subType: FeatureCollection}")]
    [TestCase("geography", "{encoding: topojson}")]
    [TestCase("geography", "{encoding: unknown}")]
    [TestCase("geography", "{algorithm: unknown}")]
    [TestCase("geography", "{orientation: clockwise}")]
    [TestCase("geography", "{epoch: unknown}")]
    [TestCase("geography", "{epoch: '2024.5'}")]
    [TestCase("geography", "{unknownOption: true}")]
    [TestCase("geography", "{bbox: [0, 0, 1]}")]
    [TestCase("geography", "{bbox: [0, bad, 1, 1]}")]
    [TestCase("geography", "{bbox: text}")]
    public void Map_InvalidOptions_ProducesActionableDiagnostics(string type, string options)
    {
        var result = Load(Field(type, options)).ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics.Any(d => d.Code == "SPATIAL_OPTIONS" && d.Severity == MappingSeverity.Error
            && d.SourcePath.EndsWith("/logicalTypeOptions", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Value!.Assets[0].Schema!.Fields[0].Extensions["odcs"]["logicalTypeOptions"], Is.Not.Null);
    }

    [TestCase("postgresql", "geometry(Point,4326)")]
    [TestCase("oracle", "SDO_GEOMETRY")]
    [TestCase("snowflake", "GEOGRAPHY")]
    public void Map_NativePhysicalTypes_RetainsBoundSourceProvider(string provider, string physicalType)
    {
        var native = Load(Field("geography", "{subType: Point, encoding: wkb}", physicalType)
            + $"\nservers:\n  - {{server: source, type: {provider}, host: localhost}}\n");
        var result = native.ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics, Is.Empty);
        var field = result.RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.PhysicalType, Is.EqualTo(physicalType));
        Assert.That(field.PhysicalTypeContext, Is.EqualTo(new PhysicalTypeContext(Provider: provider, Server: "source")));
        Assert.That(field.Spatial!.Model, Is.EqualTo(SpatialModel.Geography));
    }

    [Test]
    public void Map_UnqualifiedPhysicalType_DoesNotInferVendorFromToken()
    {
        var native = Load(Field("geography", "{encoding: wkb}", "GEOGRAPHY"));
        var result = native.ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("SPATIAL_PHYSICAL_CONTEXT"));
        Assert.That(result.Value!.Assets[0].Schema!.Fields[0].PhysicalTypeContext, Is.Null);
        var ambiguous = Load(Field("geography", "{encoding: wkb}", "GEOGRAPHY") + """

            servers:
              - {server: one, type: postgresql, host: localhost}
              - {server: two, type: snowflake, host: localhost}
            """).ToCanonicalContract(SpatialOptions);
        Assert.That(ambiguous.Diagnostics.Select(d => d.Code), Does.Contain("SPATIAL_PHYSICAL_CONTEXT"));
        Assert.That(ambiguous.Value!.Assets[0].Schema!.Fields[0].PhysicalTypeContext, Is.Null);
    }

    [Test]
    public void Map_ExplicitContext_ReusesConfigurableBindingsAndDetectsConflicts()
    {
        var native = Load(Field("geography", "{encoding: wkb}", "GEOGRAPHY"));
        var property = native.Schema[0].Properties[0];
        property.CustomProperties.Add(new() { Property = "physicalTypeContext", Vendor = "packata",
            Value = new Dictionary<string, object> { ["provider"] = "snowflake", ["dialect"] = "snowflake" } });
        var result = native.ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.RequireValue().Assets[0].Schema!.Fields[0].PhysicalTypeContext!.Provider, Is.EqualTo("snowflake"));
        var disabled = SpatialOptions with { Field = OdcsMappingOptions.NoCustomPropertyBindings.Field };
        Assert.That(native.ToCanonicalContract(disabled).Diagnostics.Single().Code, Is.EqualTo("SPATIAL_PHYSICAL_CONTEXT"));
        property.CustomProperties[0].Property = "storage";
        property.CustomProperties[0].Vendor = "acme";
        var renamed = SpatialOptions with { Field = new() { PhysicalTypeContext = OdcsBindingOverride.ForVendor("acme", "storage") } };
        Assert.That(native.ToCanonicalContract(renamed).Diagnostics, Is.Empty);
        native.Servers = Load("servers: [{server: source, type: postgresql, host: localhost}]").Servers;
        Assert.That(native.ToCanonicalContract(renamed).Diagnostics.Single().Code, Is.EqualTo("SPATIAL_PHYSICAL_CONTEXT_CONFLICT"));
    }

    [TestCase("{provider: snowflake, dialect: snowflake}", false)]
    [TestCase("{dialect: postgis}", false)]
    [TestCase("{}", true)]
    [TestCase("unknown", true)]
    public void Map_YamlPhysicalContext_UsesTypedCustomPropertyPayload(string context, bool invalid)
    {
        var native = Load(Field("geography", "{encoding: wkb}", "GEOGRAPHY")
            + $"\n        customProperties:\n          - {{property: physicalTypeContext, vendor: packata, value: {context}}}\n");
        var result = native.ToCanonicalContract(SpatialOptions);
        Assert.That(result.Diagnostics.Any(d => d.Severity == MappingSeverity.Error), Is.EqualTo(invalid));
        if (!invalid) Assert.That(result.RequireValue().Assets[0].Schema!.Fields[0].PhysicalTypeContext, Is.Not.Null);
    }

    private static string Field(string type, string options, string? physicalType = null) => $$"""
        schema:
          - name: places
            properties:
              - name: location
                logicalType: {{type}}
                physicalType: {{(physicalType is null ? "null" : "'" + physicalType + "'")}}
                logicalTypeOptions: {{options}}
                examples: ['{"type":"Point","coordinates":[4.35,50.85]}']
        """;

    private static DataContract Load(string yaml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        return new DataContractFactory().LoadFromStream(stream);
    }
}
