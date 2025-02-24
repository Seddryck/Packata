using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Tests.Serialization.Yaml
{
    public class FieldConverterTests
    {
        private class FieldCollectionWrapper
        {
            public List<Field>? Fields { get; set; }
        }

        [Test]
        public void Accepts_FieldType_ReturnsTrue()
        {
            var converter = new FieldConverter();
            Assert.That(converter.Accepts(typeof(List<Field>)), Is.True);
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
                .WithTypeConverter(new FieldConverter())
                .Build();

            var wrapper = deserializer.Deserialize<FieldCollectionWrapper>(yaml);

            Assert.That(wrapper.Fields, Is.Not.Null);
            Assert.That(wrapper.Fields.Count, Is.EqualTo(3));
            Assert.That(wrapper.Fields[0], Is.TypeOf<StringField>());
            Assert.That(wrapper.Fields[1], Is.TypeOf<NumberField>());
            Assert.That(wrapper.Fields[2], Is.TypeOf<BooleanField>());
        }
    }
}
