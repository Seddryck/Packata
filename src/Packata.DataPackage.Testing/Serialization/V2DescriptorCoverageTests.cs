using System.Text;
using NUnit.Framework;
using Packata.DataPackage.Serialization;

namespace Packata.DataPackage.Testing.Serialization;

public class V2DescriptorCoverageTests
{
    [Test]
    public void LoadFromStream_Json_PreservesRemainingV2PropertiesAndFieldTypes()
    {
        const string document = """
            {
              "sources": [{"title":"Registry","path":"https://example.com/source","version":"2026"}],
              "resources": [{
                "name":"observations",
                "homepage":"https://example.com/resource",
                "licenses":[{"name":"CC0-1.0"}],
                "sources":[{"title":"Extract","version":"2"}],
                "data":[],
                "schema":{"fields":[
                  {"name":"flag","type":"boolean","example":"yes","trueValues":["yes"],"falseValues":["no"]},
                  {"name":"point","type":"geopoint"},
                  {"name":"geometry","type":"geojson"},
                  {"name":"values","type":"array"},
                  {"name":"elapsed","type":"duration"},
                  {"name":"raw","type":"any"}
                ]}
              }]
            }
            """;

        var package = Load(document, SerializationFormat.Json);

        AssertPackage(package);
    }

    [Test]
    public void LoadFromStream_Yaml_PreservesRemainingV2PropertiesAndFieldTypes()
    {
        const string document = """
            sources:
              - title: Registry
                path: https://example.com/source
                version: "2026"
            resources:
              - name: observations
                homepage: https://example.com/resource
                licenses:
                  - name: CC0-1.0
                sources:
                  - title: Extract
                    version: "2"
                data: []
                schema:
                  fields:
                    - { name: flag, type: boolean, example: yes, trueValues: [yes], falseValues: [no] }
                    - { name: point, type: geopoint }
                    - { name: geometry, type: geojson }
                    - { name: values, type: array }
                    - { name: elapsed, type: duration }
                    - { name: raw, type: any }
            """;

        var package = Load(document, SerializationFormat.Yaml);

        AssertPackage(package);
    }

    private static DataPackage Load(string document, SerializationFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));
        return new DataPackageFactory().LoadFromStream(stream, format);
    }

    private static void AssertPackage(DataPackage package)
    {
        Assert.That(package.Sources, Has.Count.EqualTo(1));
        Assert.That(package.Sources[0].Version, Is.EqualTo("2026"));
        var resource = package.Resources.Single();
        var fields = resource.Schema!.Fields;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.Homepage, Is.EqualTo("https://example.com/resource"));
            Assert.That(resource.Licenses.Single().Name, Is.EqualTo("CC0-1.0"));
            Assert.That(resource.Sources.Single().Version, Is.EqualTo("2"));
            Assert.That(fields[0], Is.TypeOf<BooleanField>());
            Assert.That(fields[0].Example?.ToString(), Is.EqualTo("yes"));
            Assert.That(((BooleanField)fields[0]).TrueValues, Is.EqualTo(new[] { "yes" }));
            Assert.That(((BooleanField)fields[0]).FalseValues, Is.EqualTo(new[] { "no" }));
            Assert.That(fields[1], Is.TypeOf<GeoPointField>());
            Assert.That(fields[2], Is.TypeOf<GeoJsonField>());
            Assert.That(fields[3], Is.TypeOf<ArrayField>());
            Assert.That(fields[4], Is.TypeOf<DurationField>());
            Assert.That(fields[5], Is.TypeOf<AnyField>());
        }
    }
}
