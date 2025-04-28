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
        var pathFactory = new PathFactory(container.Object, new StorageProvider());
        return new PathConverter(pathFactory);
    }

    [Test]
    public void ReadYaml_ValidJsonArray_ReturnsCorrectFieldList()
    {
        var yaml = @"
            path:
              - path_01.dat
              - path_02.dat
              - path_03.dat
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.InstanceOf<ContainerPath>());
            Assert.That(wrapper.Object[1], Is.InstanceOf<ContainerPath>());
            Assert.That(wrapper.Object[2], Is.InstanceOf<ContainerPath>());
        }
    }

    [Test]
    public void ReadYaml_ValidJsonValue_ReturnsCorrectFieldList()
    {
        var yaml = @"path: path_01.dat";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<ContainerPath>());
        }
    }

    [Test]
    public void ReadYaml_ValidJsonArrayFullyQualified_ReturnsCorrectFieldList()
    {
        var yaml = @"
            path:
              - http://foo.org/path_01.dat
              - http://foo.org/path_02.dat
              - http://foo.org/path_03.dat
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.InstanceOf<FullyQualifiedPath>());
            Assert.That(wrapper.Object[1], Is.InstanceOf<FullyQualifiedPath>());
            Assert.That(wrapper.Object[2], Is.InstanceOf<FullyQualifiedPath>());
        }
    }

    [Test]
    public void ReadYaml_ValidJsonValueFullyQualified_ReturnsCorrectFieldList()
    {
        var yaml = @"path: http://foo.org/path_01.dat";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<FullyQualifiedPath>());
        }
    }
}
