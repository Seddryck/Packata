using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class PathConverterTests : AbstractConverterTests<PathConverter, List<IPath>>
{
    public PathConverterTests()
        : base("path")
    { }

    protected override PathConverter CreateConverter()
        => new (new HttpClient(), "c:\\");

    [Test]
    public void ReadJson_ValidJsonArray_ReturnsCorrectFieldList()
    {
        var yaml = @"
            path:
              - path_01
              - path_02
              - path_03
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Object[1], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Object[2], Is.InstanceOf<IPath>());
        }
    }

    [Test]
    public void ReadJson_ValidJsonValue_ReturnsCorrectFieldList()
    {
        var yaml = @"path: path_01";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<IPath>());
        }
    }
}
