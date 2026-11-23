using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;

namespace Packata.DataPackage.Testing.Mapping;

public class SpatialFieldMappingTests
{
    private const string JsonDocument = """
        {
          "name": "places",
          "resources": [{
            "name": "places",
            "path": "places.csv",
            "schema": {"fields": [
              {"name": "point", "type": "geopoint"},
              {"name": "text_point", "type": "geopoint", "format": "default"},
              {"name": "array_point", "type": "geopoint", "format": "array"},
              {"name": "object_point", "type": "geopoint", "format": "object"},
              {"name": "geometry", "type": "geojson"},
              {"name": "feature", "type": "geojson", "format": "default"},
              {"name": "topology", "type": "geojson", "format": "topojson"}
            ]}
          }]
        }
        """;

    private const string YamlDocument = """
        name: places
        resources:
          - name: places
            path: places.csv
            schema:
              fields:
                - { name: point, type: geopoint }
                - { name: text_point, type: geopoint, format: default }
                - { name: array_point, type: geopoint, format: array }
                - { name: object_point, type: geopoint, format: object }
                - { name: geometry, type: geojson }
                - { name: feature, type: geojson, format: default }
                - { name: topology, type: geojson, format: topojson }
        """;

    [TestCase(SerializationFormat.Json)]
    [TestCase(SerializationFormat.Yaml)]
    public void Map_SpatialDescriptors_PreservesFormatsAndResolvesDefaultClrTypes(SerializationFormat format)
    {
        var document = format == SerializationFormat.Json ? JsonDocument : YamlDocument;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));
        var package = new DataPackageFactory().LoadFromStream(stream, format);

        var mapping = package.ToCanonicalContract();
        var fields = mapping.RequireValue().Assets.Single().Schema!.Fields;

        Assert.That(mapping.Diagnostics.Select(d => d.Code),
            Is.EqualTo(new[] { "SPATIAL_RFC0048_CONTENT", "SPATIAL_RFC0048_CONTENT", "SPATIAL_RFC0048_ENCODING" }));
        Assert.That(fields.Select(field => field.LogicalType),
            Is.EqualTo(new[] { "geopoint", "geopoint", "geopoint", "geopoint", "geojson", "geojson", "geojson" }));
        Assert.That(fields.Select(field => field.Format),
            Is.EqualTo(new string?[] { null, "default", "array", "object", null, "default", "topojson" }));
        foreach (var field in fields)
        {
            var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(resolution.RuntimeType,
                    Is.EqualTo(field.LogicalType == "geopoint" ? typeof(GeoPoint) : typeof(JsonElement)));
                Assert.That(resolution.Diagnostics.Any(d => d.Target == TypeMappingTarget.Source), Is.False);
                Assert.That(resolution.DatabaseType, Is.Null);
                Assert.That(field.Spatial, Is.Not.Null);
                Assert.That(field.Spatial!.Model, Is.EqualTo(SpatialModel.Unspecified));
                Assert.That(field.Spatial.Crs, Is.Null);
            }
        }
        Assert.That(fields.Take(4).All(field => field.Spatial!.SubType == SpatialSubtype.Point && field.Spatial.Dimensions == 2), Is.True);
        Assert.That(fields.Take(4).All(field => field.Spatial!.Encoding is null), Is.True);
        Assert.That(fields[4].Spatial!.Content, Is.EqualTo(SpatialContent.GeoJsonValue));
        Assert.That(fields[6].Spatial!.Content, Is.EqualTo(SpatialContent.Topology));
        Assert.That(fields[6].Spatial!.Encoding, Is.EqualTo(SpatialEncoding.TopoJson));
    }

    [TestCase(SerializationFormat.Json)]
    [TestCase(SerializationFormat.Yaml)]
    public void Map_ExplicitPhysicalContext_PreservesNamespacedMetadataAndSupportsBindingOverrides(SerializationFormat format)
    {
        const string json = """
            {"name":"places","resources":[{"name":"places","path":"places.csv","schema":{"fields":[
              {"name":"location","type":"geopoint","x-packata-physicalType":"GEOGRAPHY",
               "packata:customProperties":[{"property":"physicalTypeContext","value":{"provider":"snowflake","dialect":"snowflake"}}]}
            ]}}]}
            """;
        const string yaml = """
            name: places
            resources:
              - name: places
                path: places.csv
                schema:
                  fields:
                    - name: location
                      type: geopoint
                      x-packata-physicalType: GEOGRAPHY
                      packata:customProperties:
                        - property: physicalTypeContext
                          value: {provider: snowflake, dialect: snowflake}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(format == SerializationFormat.Json ? json : yaml));
        var native = new DataPackageFactory().LoadFromStream(stream, format);
        var result = native.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        var field = result.RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.PhysicalType, Is.EqualTo("GEOGRAPHY"));
        Assert.That(field.PhysicalTypeContext, Is.EqualTo(new PhysicalTypeContext("snowflake", "snowflake")));
        Assert.That(field.Spatial!.Model, Is.EqualTo(SpatialModel.Unspecified));
        Assert.That(native.ToCanonicalContract(DataPackageMappingOptions.NoCustomPropertyBindings).Diagnostics.Single().Code,
            Is.EqualTo("SPATIAL_PHYSICAL_CONTEXT"));
        native.Resources[0].Schema!.Fields[0].CustomProperties![0].Property = "storage";
        Assert.That(native.ToCanonicalContract(new() { PhysicalTypeContext = DataPackageBindingOverride.FromProperty("storage") }).Diagnostics, Is.Empty);
    }
}
