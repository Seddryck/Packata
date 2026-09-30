using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Storage;
using Packata.Core.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Testing.Serialization.Json;

internal class PathConverterTests : AbstractConverterTests<PathConverter, List<IPath>>
{
    public PathConverterTests()
        : base("path")
    { }

    protected override PathConverter CreateConverter()
        => new (new PathFactory(new LocalDirectoryDataPackageContainer(), new StorageProvider()));

    [Test]
    public void ReadJson_ValidJsonArrayRelative_ReturnsCorrectFieldList()
    {
        var json = @"{""path"":
            [
                ""path_01.dat"",
                ""path_02.dat"",
                ""path_03.dat""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

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
    public void ReadJson_ValidJsonValueRelative_ReturnsCorrectFieldList()
    {
        var json = @"{""path"": ""path_01.dat""}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<ContainerPath>());
        }
    }

    [Test]
    public void ReadJson_ValidJsonArrayFullyQualified_ReturnsCorrectFieldList()
    {
        var json = @"{""path"":
            [
                ""http://foo.org/path_01.dat"",
                ""http://bar.com/path_02.dat"",
                ""http://foo.org/path_03.dat""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

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
    public void ReadJson_ValidJsonValueFullyQualified_ReturnsCorrectFieldList()
    {
        var json = @"{""path"": ""http://foo.org/path_01.dat""}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<FullyQualifiedPath>());
        }
    }
}
