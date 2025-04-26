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
        => new (new PathFactory(new LocalDirectoryDataPackageContainer()));

    [Test]
    public void ReadJson_ValidJsonArray_ReturnsCorrectFieldList()
    {
        var json = @"{""path"":
            [
                ""path_01"",
                ""path_02"",
                ""path_03""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

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
        var json = @"{""path"": ""path_01""}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(1));
            Assert.That(wrapper.Object[0], Is.InstanceOf<IPath>());
        }
    }

    
}
