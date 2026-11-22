using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class SpatialDescriptorTests
{
    [TestCase("geometry", SpatialModel.Geometry, SpatialEncoding.GeoJson, typeof(JsonElement))]
    [TestCase("geography", SpatialModel.Geography, SpatialEncoding.GeoJson, typeof(JsonElement))]
    [TestCase("geometry", SpatialModel.Geometry, SpatialEncoding.Wkt, typeof(string))]
    [TestCase("geography", SpatialModel.Geography, SpatialEncoding.Ewkt, typeof(string))]
    [TestCase("geometry", SpatialModel.Geometry, SpatialEncoding.Wkb, typeof(byte[]))]
    [TestCase("geography", SpatialModel.Geography, SpatialEncoding.Ewkb, typeof(byte[]))]
    public void Resolve_DeclaredEncoding_SelectsClrRepresentation(string type, SpatialModel model, SpatialEncoding encoding, Type expected)
    {
        var field = new DataField("location", type)
        {
            Spatial = new(model, SpatialSubtype.Point, "EPSG:4326", 3, encoding)
        };

        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuntimeType, Is.EqualTo(expected));
            Assert.That(result.DatabaseType, Is.Null);
            Assert.That(result.Diagnostics.Select(d => d.Target), Is.EqualTo(new[] { TypeMappingTarget.Database }));
            Assert.That(result.Field.Spatial, Is.SameAs(field.Spatial));
        }
    }

    [TestCase("geometry", SpatialModel.Geometry)]
    [TestCase("geography", SpatialModel.Geography)]
    public void Resolve_UnspecifiedEncoding_DiagnosesUnavailableRuntime(string type, SpatialModel model)
    {
        var result = FieldTypeResolver.Resolve(new("location", type)
        { Spatial = new(model, SpatialSubtype.Point, "EPSG:4326") }, TypeMappingOptions.Strict);

        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Select(d => (d.Code, d.Target)), Is.EquivalentTo(new[]
        {
            ("UnsupportedRepresentation", TypeMappingTarget.Runtime),
            ("UnsupportedRepresentation", TypeMappingTarget.Database)
        }));
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void Descriptor_JsonRoundTrip_PreservesDeclaredSpatialAndPhysicalMetadata(int dimensions)
    {
        var bounds = Enumerable.Range(0, dimensions * 2).Select(value => (double)value).ToArray();
        var field = new DataField("shape", "geography", PhysicalType: "GEOGRAPHY")
        {
            Spatial = new(SpatialModel.Geography, SpatialSubtype.Point, "OGC:CRS84", dimensions,
                SpatialEncoding.Wkb, Algorithm: "karney", Orientation: "counterclockwise", Epoch: 2021.47)
            { BoundingBox = bounds },
            PhysicalTypeContext = new("snowflake", "snowflake", "warehouse")
        };

        var copy = JsonSerializer.Deserialize<DataField>(JsonSerializer.Serialize(field))!;
        bounds[0] = -999;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copy.Spatial!.Model, Is.EqualTo(SpatialModel.Geography));
            Assert.That(copy.Spatial.SubType, Is.EqualTo(SpatialSubtype.Point));
            Assert.That(copy.Spatial.Crs, Is.EqualTo("OGC:CRS84"));
            Assert.That(copy.Spatial.Dimensions, Is.EqualTo(dimensions));
            Assert.That(copy.Spatial.Encoding, Is.EqualTo(SpatialEncoding.Wkb));
            Assert.That(copy.Spatial.Algorithm, Is.EqualTo("karney"));
            Assert.That(copy.Spatial.Orientation, Is.EqualTo("counterclockwise"));
            Assert.That(copy.Spatial.Epoch, Is.EqualTo(2021.47));
            Assert.That(copy.Spatial.BoundingBox, Is.EqualTo(field.Spatial.BoundingBox));
            Assert.That(field.Spatial.BoundingBox![0], Is.Zero);
            Assert.That(copy.PhysicalType, Is.EqualTo("GEOGRAPHY"));
            Assert.That(copy.PhysicalTypeContext, Is.EqualTo(field.PhysicalTypeContext));
        }
    }

    [Test]
    public void Resolve_ConflictingSpatialModel_ReportsSourceError()
    {
        var field = new DataField("location", "geometry")
        { Spatial = new(SpatialModel.Geography, Encoding: SpatialEncoding.Wkt) };

        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);

        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "ConflictingSemantics" && d.Target == TypeMappingTarget.Source), Is.True);
    }

    [TestCase(SpatialContent.Topology, SpatialEncoding.TopoJson, 2)]
    [TestCase(SpatialContent.GeoJsonValue, SpatialEncoding.GeoJson, 2)]
    [TestCase(SpatialContent.Geometry, SpatialEncoding.Wkt, 5)]
    public void Resolve_InvalidSpatialDescriptor_DoesNotAdvertiseDefaultRepresentation(SpatialContent content, SpatialEncoding encoding, int dimensions)
    {
        var field = new DataField("location", "geometry")
        { Spatial = new(SpatialModel.Geometry, Crs: "EPSG:4326", Dimensions: dimensions, Encoding: encoding, Content: content) };
        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Target == TypeMappingTarget.Source), Is.True);
    }

    [Test]
    public void Resolve_GeoPointWithConflictingDimensions_DiagnosesSource()
    {
        var field = new DataField("location", "geopoint") { Spatial = new(Dimensions: 3) };
        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "ConflictingSemantics"), Is.True);
    }
}
