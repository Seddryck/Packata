using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core;
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

        Assert.That(mapping.Diagnostics, Is.Empty);
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
            }
        }
    }
}
