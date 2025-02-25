using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

public class FieldTypeDiscriminatorTests
{
    private class FieldCollectionWrapper
    {
        public List<Field>? Fields { get; set; }
    }

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

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeDiscriminatingNodeDeserializer((o) => new FieldTypeDiscriminator().Execute(o))
            .Build();

        var wrapper = deserializer.Deserialize<FieldCollectionWrapper>(yaml);

        Assert.That(wrapper.Fields, Is.Not.Null);
        Assert.That(wrapper.Fields.Count, Is.EqualTo(3));
        Assert.That(wrapper.Fields[0], Is.TypeOf<StringField>());
        Assert.That(wrapper.Fields[1], Is.TypeOf<NumberField>());
        Assert.That(wrapper.Fields[2], Is.TypeOf<BooleanField>());
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

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeDiscriminatingNodeDeserializer((o) => new FieldTypeDiscriminator().Execute(o))
            .Build();

        var wrapper = deserializer.Deserialize<FieldCollectionWrapper>(yaml);

        Assert.That(wrapper.Fields, Is.Not.Null);
        Assert.That(wrapper.Fields.Count, Is.EqualTo(2));
        Assert.That(wrapper.Fields[0], Is.TypeOf<StringField>());
        Assert.That(wrapper.Fields[1], Is.TypeOf<Field>());
    }
}
