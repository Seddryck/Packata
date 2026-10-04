using Newtonsoft.Json;
using NUnit.Framework;
using Packata.DataPackage.Serialization.Json;

namespace Packata.DataPackage.Testing.Serialization.Json;

internal class ConstraintsConverterTests : BaseConverterTests<ConstraintsConverter, FieldConstraintCollection>
{
    public ConstraintsConverterTests() : base("constraints") { }

    [Test]
    public void ReadJson_StructuredConstraints_PreservesValues()
    {
        const string json = """
            {"constraints":{"enum":["apple",2,true,null],"jsonSchema":{"type":"object","properties":{"value":{"type":"integer"}}}}}
            """;

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object, Has.Count.EqualTo(2));
        var enumeration = wrapper.Object![0] as EnumConstraint;
        var schema = wrapper.Object[1] as JsonSchemaConstraint;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(enumeration, Is.Not.Null);
            Assert.That(enumeration!.Value, Is.EqualTo(new object?[] { "apple", 2L, true, null }));
            Assert.That(schema, Is.Not.Null);
            Assert.That(schema!.Value["type"], Is.EqualTo("object"));
            Assert.That(schema.Value["properties"], Is.InstanceOf<IReadOnlyDictionary<string, object?>>());
        }
    }
}
