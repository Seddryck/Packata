using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;
using Packata.Core.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Testing.Serialization.Json;

internal class SingleOrArrayConverterTests : BaseConverterTests<SingleOrArrayConverter, List<string>>
{
    public SingleOrArrayConverterTests()
        : base("path")
    { }

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
            Assert.That(wrapper.Object[0], Is.EqualTo("path_01"));
            Assert.That(wrapper.Object[1], Is.EqualTo("path_02"));
            Assert.That(wrapper.Object[2], Is.EqualTo("path_03"));
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
            Assert.That(wrapper.Object[0], Is.EqualTo("path_01"));
        }
    }
}
