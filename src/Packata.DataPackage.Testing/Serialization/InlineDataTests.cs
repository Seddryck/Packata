using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;

namespace Packata.DataPackage.Testing.Serialization;

[TestFixture(SerializationFormat.Json)]
[TestFixture(SerializationFormat.Yaml)]
public class InlineDataTests(SerializationFormat format)
{
    private DataPackage Load(string json, string yaml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            format == SerializationFormat.Json ? json : yaml));
        return new DataPackageFactory().LoadFromStream(stream, format);
    }

    [Test]
    public void Deserialize_InlineRecords_ExposesDictionariesWithNativeValues()
    {
        var package = Load(
            """
            {"resources":[{"name":"people","data":[
              {"id":1,"Name":"Alice","score":1.5,"active":true,"missing":null,
               "code":"001","details":{"city":"Brussels"},"tags":["a","b"]},
              {"id":2,"Name":"Bob"}
            ]}]}
            """,
            """
            resources:
              - name: people
                data:
                  - id: 1
                    Name: Alice
                    score: 1.5
                    active: true
                    missing: null
                    code: "001"
                    details: {city: Brussels}
                    tags: [a, b]
                  - id: 2
                    Name: Bob
            """);

        Assert.That(package.Resources[0].Data, Is.InstanceOf<IEnumerable<IDictionary<string, object?>>>());
        var rows = ((IEnumerable<IDictionary<string, object?>>)package.Resources[0].Data!).ToArray();
        Assert.That(rows, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0]["id"], Is.EqualTo(1));
            Assert.That(rows[0]["Name"], Is.EqualTo("Alice"));
            Assert.That(rows[0]["score"], Is.EqualTo(1.5));
            Assert.That(rows[0]["active"], Is.EqualTo(true));
            Assert.That(rows[0]["missing"], Is.Null);
            Assert.That(rows[0]["code"], Is.EqualTo("001"));
            Assert.That(rows[0]["details"], Is.InstanceOf<IDictionary<string, object?>>());
            Assert.That(rows[0]["tags"], Is.EqualTo(new[] { "a", "b" }));
            Assert.That(rows[1]["id"], Is.EqualTo(2));
            Assert.That(rows[1]["Name"], Is.EqualTo("Bob"));
        });
        Assert.That(((IDictionary<string, object?>)rows[0]["details"]!)["city"], Is.EqualTo("Brussels"));

        var contract = package.ToCanonicalContract().RequireValue();
        var location = (InlineLocation)contract.Endpoints.Single().Location;
        Assert.That(location.Value, Is.EqualTo(rows));
    }

    [Test]
    public void Deserialize_EmptyInlineRecords_ExposesEmptyDictionarySequence()
    {
        var package = Load("""{"resources":[{"name":"empty","data":[]}]}""",
            """
            resources:
              - name: empty
                data: []
            """);
        Assert.That(package.Resources[0].Data, Is.InstanceOf<IEnumerable<IDictionary<string, object?>>>());
        Assert.That((IEnumerable<IDictionary<string, object?>>)package.Resources[0].Data!, Is.Empty);
    }

    [Test]
    public void Deserialize_AbsentOrNullInlineData_RemainsNull()
    {
        var package = Load("""{"resources":[{"name":"absent"},{"name":"null","data":null}]}""",
            """
            resources:
              - name: absent
              - name: null-data
                data: null
            """);
        Assert.That(package.Resources.Select(resource => resource.Data), Is.All.Null);
    }

    [Test]
    public void Deserialize_OtherInlineShapes_PreservesValues()
    {
        var package = Load(
            """{"resources":[{"data":[[1,"Alice"],[2,"Bob"]]},{"data":"text"},{"data":{"key":"value"}}]}""",
            """
            resources:
              - data: [[1, Alice], [2, Bob]]
              - data: text
              - data: {key: value}
            """);
        Assert.Multiple(() =>
        {
            Assert.That(package.Resources[0].Data, Is.EqualTo(new object[]
            {
                new object[] { 1, "Alice" }, new object[] { 2, "Bob" }
            }));
            Assert.That(package.Resources[1].Data, Is.EqualTo("text"));
            Assert.That(package.Resources[2].Data, Is.InstanceOf<IDictionary<string, object?>>());
        });
    }
}
