using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml
{
    public class PathConverterTests
    {
        private class PathWrapper
        {
            public List<IPath>? Path { get; set; }
        }

        [Test]
        public void Accepts_FieldType_ReturnsTrue()
        {
            var converter = new PathConverter(new HttpClient(), "c:\\");
            Assert.That(converter.Accepts(typeof(List<IPath>)), Is.True);
        }

        [Test]
        public void ReadJson_ValidJsonArray_ReturnsCorrectFieldList()
        {
            var yaml = @"
            path:
              - path_01
              - path_02
              - path_03
            ";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new PathConverter(new HttpClient(), "c:\\"))
                .Build();

            var wrapper = deserializer.Deserialize<PathWrapper>(yaml);

            Assert.That(wrapper?.Path, Is.Not.Null);
            Assert.That(wrapper.Path, Has.Count.EqualTo(3));
            Assert.That(wrapper.Path[0], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Path[1], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Path[2], Is.InstanceOf<IPath>());
        }

        [Test]
        public void ReadJson_ValidJsonValue_ReturnsCorrectFieldList()
        {
            var yaml = @"path: path_01";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new PathConverter(new HttpClient(), "c:\\"))
                .Build();

            var wrapper = deserializer.Deserialize<PathWrapper>(yaml);

            Assert.That(wrapper?.Path, Is.Not.Null);
            Assert.That(wrapper.Path, Has.Count.EqualTo(1));
            Assert.That(wrapper.Path[0], Is.InstanceOf<IPath>());
        }
    }
}
