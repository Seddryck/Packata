using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class SingleOrArrayConverterTests : BaseConverterTests<SingleOrArrayConverter, List<string>>
{
    public SingleOrArrayConverterTests()
        : base("path")
    { }

    [Test]
    public void ReadJson_ValidYamlArray_ReturnsCorrectPathList()
    {
        var yaml = @"
            path:
              - path_01
              - path_02
              - path_03
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.EqualTo("path_01"));
            Assert.That(wrapper.Object[1], Is.EqualTo("path_02"));
            Assert.That(wrapper.Object[2], Is.EqualTo("path_03"));
        });
    }

    [Test]
    public void ReadJson_ValidYamlValue_ReturnsCorrectPathList()
    {
        var yaml = @"path: path_01";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.EqualTo("path_01"));
        }
    }
}
