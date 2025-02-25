using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

public class SingleOrArrayConverterTests
{
    private class PathCollectionWrapper
    {
        public List<string>? Path { get; set; }
    }

    [Test]
    public void Accepts_FieldType_ReturnsTrue()
    {
        var converter = new SingleOrArrayConverter();
        Assert.That(converter.Accepts(typeof(List<string>)), Is.True);
    }

    [Test]
    public void ReadJson_ValidYamlArray_ReturnsCorrectPathList()
    {
        var yaml = @"
            path:
              - path_01
              - path_02
              - path_03
            ";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SingleOrArrayConverter())
            .Build();

        var wrapper = deserializer.Deserialize<PathCollectionWrapper>(yaml);

        Assert.That(wrapper?.Path, Is.Not.Null);
        Assert.That(wrapper.Path, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Path[0], Is.EqualTo("path_01"));
            Assert.That(wrapper.Path[1], Is.EqualTo("path_02"));
            Assert.That(wrapper.Path[2], Is.EqualTo("path_03"));
        });
    }

    [Test]
    public void ReadJson_ValidYamlValue_ReturnsCorrectPathList()
    {
        var yaml = @"path: path_01";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SingleOrArrayConverter())
            .Build();

        var wrapper = deserializer.Deserialize<PathCollectionWrapper>(yaml);

        Assert.That(wrapper?.Path, Is.Not.Null);
        Assert.That(wrapper.Path, Has.Count.EqualTo(1));
        Assert.That(wrapper.Path[0], Is.EqualTo("path_01"));
    }
}
