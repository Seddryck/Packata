using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using Moq;
using Packata.Core.Storage;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class PathConverterTests : AbstractConverterTests<PathConverter, List<IPath>>
{
    public PathConverterTests()
        : base("path")
    { }

    protected override PathConverter CreateConverter()
    {
        var container = new Mock<IDataPackageContainer>();
        container.Setup(c => c.BaseUri).Returns(new Uri("file://c:/"));
        var pathFactory = new PathFactory(container.Object);
        return new PathConverter(pathFactory);
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
