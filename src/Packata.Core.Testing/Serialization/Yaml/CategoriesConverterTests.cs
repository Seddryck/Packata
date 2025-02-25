using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml
{
    public class CategoriesConverterTests
    {
        private class CategoriesWrapper
        {
            public List<ICategory>? Categories { get; set; }
        }

        [Test]
        public void Accepts_FieldType_ReturnsTrue()
        {
            var converter = new CategoriesConverter();
            Assert.That(converter.Accepts(typeof(List<ICategory>)), Is.True);
        }

        [Test]
        public void ReadJson_ValidYamlArray_ReturnsCorrectPathList()
        {
            var yaml = @"
            categories:
              - apple
              - orange
              - banana
            ";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new CategoriesConverter())
                .Build();

            var wrapper = deserializer.Deserialize<CategoriesWrapper>(yaml);

            Assert.That(wrapper?.Categories, Is.Not.Null);
            Assert.That(wrapper.Categories, Has.Count.EqualTo(3));
            Assert.That(wrapper.Categories, Is.All.InstanceOf<CategoryLabel>());
            Assert.That(wrapper.Categories[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Categories[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Categories[2].Label, Is.EqualTo("banana"));
        }

        [Test]
        public void ReadJson_ValidYamlValue_ReturnsCorrectPathList()
        {
            var yaml = @"
            categories:
              - value: 0
                label: apple
              - value: 1
                label: orange
              - value: 2
                label: banana
            ";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new CategoriesConverter())
                .Build();

            var wrapper = deserializer.Deserialize<CategoriesWrapper>(yaml);

            Assert.That(wrapper?.Categories, Is.Not.Null);
            Assert.That(wrapper.Categories, Has.Count.EqualTo(3));
            Assert.That(wrapper.Categories, Is.All.InstanceOf<Category>());
            Assert.That(wrapper.Categories[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Categories[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Categories[2].Label, Is.EqualTo("banana"));
            Assert.That(((Category)wrapper.Categories[0]).Value, Is.EqualTo(0));
            Assert.That(((Category)wrapper.Categories[1]).Value, Is.EqualTo(1));
            Assert.That(((Category)wrapper.Categories[2]).Value, Is.EqualTo(2));
        }
    }
}
