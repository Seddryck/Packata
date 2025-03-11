using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization.Json;

internal class ResourcesConverterTests : AbstractConverterTests<ResourcesConverter, List<Resource>>
{
    public ResourcesConverterTests()
        : base("resources")
    { }

    protected override ResourcesConverter CreateConverter()
        => new("c:\\");

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectCategoryList()
    {
        var json = @"{""resources"":
            [
                {""name"": ""resource_01""},
                {""name"": ""resource_02""},
                {""name"": ""resource_03""}
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<Resource>());
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object[0].RootPath, Is.EqualTo("c:\\"));
            Assert.That(wrapper.Object[1].RootPath, Is.EqualTo("c:\\"));
            Assert.That(wrapper.Object[2].RootPath, Is.EqualTo("c:\\"));
        }
    }
}
