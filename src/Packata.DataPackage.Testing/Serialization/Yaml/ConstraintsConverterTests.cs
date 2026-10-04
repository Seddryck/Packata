using NUnit.Framework;
using Packata.DataPackage.Serialization.Yaml;

namespace Packata.DataPackage.Testing.Serialization.Yaml;

internal class ConstraintsConverterTests : BaseConverterTests<ConstraintsConverter, FieldConstraintCollection>
{
    public ConstraintsConverterTests() : base("constraints") { }

    [Test]
    public void ReadYaml_StructuredConstraints_PreservesValues()
    {
        const string yaml = """
            constraints:
              enum: [apple, 2, true, null]
              jsonSchema:
                type: object
                properties:
                  value:
                    type: integer
            """;

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper.Object, Has.Count.EqualTo(2));
        var enumeration = wrapper.Object![0] as EnumConstraint;
        var schema = wrapper.Object[1] as JsonSchemaConstraint;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(enumeration, Is.Not.Null);
            Assert.That(enumeration!.Value.Take(3).Select(Convert.ToString), Is.EqualTo(new[] { "apple", "2", "True" }));
            Assert.That(enumeration.Value[3], Is.Null);
            Assert.That(schema, Is.Not.Null);
            Assert.That(schema!.Value["type"], Is.EqualTo("object"));
            Assert.That(schema.Value["properties"], Is.InstanceOf<IReadOnlyDictionary<string, object?>>());
        }
    }
}
