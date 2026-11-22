using System.Data;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class SpatialFieldTypeResolverTests
{
    [TestCase("geopoint", null, typeof(GeoPoint))]
    [TestCase("geopoint", "default", typeof(GeoPoint))]
    [TestCase("geopoint", "array", typeof(GeoPoint))]
    [TestCase("geopoint", "object", typeof(GeoPoint))]
    [TestCase("geojson", null, typeof(JsonElement))]
    [TestCase("geojson", "default", typeof(JsonElement))]
    [TestCase("geojson", "topojson", typeof(JsonElement))]
    [TestCase(" GEOPOINT ", " ARRAY ", typeof(GeoPoint))]
    [TestCase(" GEOJSON ", " DEFAULT ", typeof(JsonElement))]
    public void Resolve_SupportedFormat_PreservesRuntimeAndReportsUnavailableDatabase(
        string type, string? format, Type expectedRuntime)
    {
        var field = new DataField("location", type, "geometry", format, Required: true, Nullable: true);
        var options = new TypeMappingOptions(SourcePath: "places/location");
        var resolutions = new[]
        {
            FieldTypeResolver.Resolve(field, options),
            new RuntimeTypeMapper().Resolve(field, options),
            new DbTypeMapper().Resolve(field, options)
        };

        foreach (var resolution in resolutions)
        {
            var diagnostic = resolution.Diagnostics.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(resolution.RuntimeType, Is.EqualTo(expectedRuntime));
                Assert.That(resolution.DatabaseType, Is.Null);
                Assert.That(resolution.Field, Is.SameAs(field));
                Assert.That(resolution.LogicalType, Is.EqualTo(type.Trim().ToLowerInvariant()));
                Assert.That(resolution.SourceFormat, Is.EqualTo(format));
                Assert.That(resolution.Required, Is.True);
                Assert.That(resolution.Nullable, Is.True);
                Assert.That(diagnostic.Code, Is.EqualTo("UnsupportedRepresentation"));
                Assert.That(diagnostic.Target, Is.EqualTo(TypeMappingTarget.Database));
                Assert.That(diagnostic.Severity, Is.EqualTo(TypeMappingSeverity.Error));
                Assert.That(diagnostic.FieldPath, Is.EqualTo("places/location"));
                Assert.That(diagnostic.LogicalType, Is.EqualTo(type));
                Assert.That(diagnostic.Format, Is.EqualTo(format));
                Assert.That(diagnostic.PhysicalType, Is.EqualTo("geometry"));
            }
        }
    }

    [TestCase("geopoint", "topojson")]
    [TestCase("geopoint", "wkt")]
    [TestCase("geopoint", "any")]
    [TestCase("geopoint", "")]
    [TestCase("geojson", "array")]
    [TestCase("geojson", "object")]
    [TestCase("geojson", "wkt")]
    [TestCase("geojson", "any")]
    [TestCase("geojson", "")]
    public void Resolve_InvalidSpatialFormat_ReportsSourceError(string type, string format)
    {
        var result = FieldTypeResolver.Resolve(new("location", type, Format: format), TypeMappingOptions.Strict);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuntimeType, Is.Null);
            Assert.That(result.DatabaseType, Is.Null);
            Assert.That(result.Diagnostics.Select(d => (d.Code, d.Target)),
                Is.EqualTo(new[] { ("UnsupportedFormat", TypeMappingTarget.Source) }));
        }
    }

    [TestCase("geopoint", "array")]
    [TestCase("geojson", "topojson")]
    public void Resolve_CustomRuntimeRegistration_PreservesRecognizedSpatialDeclaration(string type, string format)
    {
        var mapper = new RuntimeTypeMapper();
        mapper.Register(type, typeof(string));

        var result = mapper.Resolve(new("location", type, Format: format), TypeMappingOptions.Strict);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuntimeType, Is.EqualTo(typeof(string)));
            Assert.That(result.Diagnostics.Any(d => d.Code == "CustomRegistration"
                && d.Target == TypeMappingTarget.Runtime), Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Target == TypeMappingTarget.Source), Is.False);
        }
    }

    [TestCase("geopoint", "array")]
    [TestCase("geojson", "topojson")]
    public void Resolve_LegacyPolicy_PreservesExistingOpaqueReaderRepresentation(string type, string format)
    {
        var field = new DataField("location", type, Format: format);
        var mapper = new RuntimeTypeMapper();
        var resolution = mapper.Resolve(field, TypeMappingOptions.LegacyCompatibility);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapper.Map(type, format), Is.EqualTo(typeof(object)));
            Assert.That(resolution.RuntimeType, Is.EqualTo(typeof(object)));
            Assert.That(FieldTypeResolver.Resolve(field, TypeMappingOptions.LegacyCompatibility).RuntimeType,
                Is.EqualTo(typeof(object)));
            Assert.That(new DbTypeMapper().Map(type, format), Is.EqualTo(DbType.Object));
            Assert.That(resolution.DatabaseType, Is.EqualTo(DbType.Object));
            Assert.That(resolution.Diagnostics.Any(d => d.Code == "LegacyRepresentation"
                && d.Target == TypeMappingTarget.Runtime), Is.True);
        }
    }

    [TestCase(TypeMappingFallback.LogicalType, DbType.Object)]
    [TestCase(TypeMappingFallback.Object, DbType.Object)]
    [TestCase(TypeMappingFallback.String, DbType.String)]
    public void Resolve_DatabaseFallback_KeepsTheSupportedRuntime(TypeMappingFallback fallback, DbType database)
    {
        var result = FieldTypeResolver.Resolve(new("location", "geopoint"), new(fallback));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuntimeType, Is.EqualTo(typeof(GeoPoint)));
            Assert.That(result.DatabaseType, Is.EqualTo(database));
            Assert.That(result.Diagnostics.Any(d => d.Code == "UnsupportedRepresentation"), Is.True);
            Assert.That(result.Diagnostics.Where(d => d.Code == "Fallback").Select(d => d.Target),
                Is.EqualTo(new[] { TypeMappingTarget.Database }));
        }
    }

    [TestCase("geopoint")]
    [TestCase("geojson")]
    public void Resolve_SpatialFieldWithItems_RejectsInconsistentShape(string type)
    {
        var field = new DataField("location", type, Items: new("coordinate", "number"));

        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);

        Assert.That(result.RuntimeType, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "UnsupportedShape"
            && d.Target == TypeMappingTarget.Source), Is.True);
    }

    [Test]
    public void Resolve_ArrayOfGeoPoints_PreservesNestedRuntimeAndDiagnosticPath()
    {
        var field = new DataField("locations", "array", Items: new("location", "geopoint", Format: "array"));

        var result = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuntimeType, Is.EqualTo(typeof(GeoPoint[])));
            Assert.That(result.Items!.RuntimeType, Is.EqualTo(typeof(GeoPoint)));
            Assert.That(result.Diagnostics.Any(d => d.Code == "UnsupportedRepresentation"
                && d.FieldPath == "locations/items" && d.Target == TypeMappingTarget.Database), Is.True);
        }
    }

    [Test]
    public void GeoPoint_NamedCoordinates_HaveValueEquality()
    {
        var point = new GeoPoint(Longitude: 4.35, Latitude: 50.85);
        var (longitude, latitude) = point;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(longitude, Is.EqualTo(4.35));
            Assert.That(latitude, Is.EqualTo(50.85));
            Assert.That(point, Is.EqualTo(new GeoPoint(4.35, 50.85)));
            Assert.That(point, Is.Not.EqualTo(new GeoPoint(50.85, 4.35)));
            Assert.That(typeof(GeoPoint).IsValueType, Is.True);
            Assert.That(typeof(GeoPoint).GetProperty(nameof(GeoPoint.Longitude))!.PropertyType, Is.EqualTo(typeof(double)));
            Assert.That(typeof(GeoPoint).GetProperty(nameof(GeoPoint.Latitude))!.PropertyType, Is.EqualTo(typeof(double)));
        }
    }
}
