using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class FieldTypeDiscriminatorTests : BaseTypeDiscriminatorTests<FieldTypeDiscriminator, List<Field>>
{
    public FieldTypeDiscriminatorTests()
        : base("fields")
    { }

    [Test]
    public void ReadJson_ValidJson_ReturnsCorrectFieldList()
    {
        var yaml = @"
            fields:
              - type: ""string""
                name: ""test""
              - type: ""number""
                name: 123
              - type: ""boolean""
                name: true
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.TypeOf<StringField>());
            Assert.That(wrapper.Object[1], Is.TypeOf<NumberField>());
            Assert.That(wrapper.Object[2], Is.TypeOf<BooleanField>());
        }
    }

    [Test]
    public void ReadJson_ValidJsonWithNoType_ReturnsCorrectFieldList()
    {
        var yaml = @"
            fields:
              - type: ""string""
                name: ""test""
              - name: 123
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper.Object, Is.Not.Null);
        Assert.That(wrapper.Object, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object[0], Is.TypeOf<StringField>());
            Assert.That(wrapper.Object[1], Is.TypeOf<Field>());
        }
    }
}
