using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization.Json;

internal class CategoriesConverterTests : BaseConverterTests<CategoriesConverter, List<ICategory>>
{
    public CategoriesConverterTests()
        : base("categories")
    { }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectCategoryList()
    {
        var json = @"{""categories"":
            [
                ""apple"",
                ""orange"",
                ""banana""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<CategoryLabel>());
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Object[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Object[2].Label, Is.EqualTo("banana"));
        }
    }

    [Test]
    public void ReadJson_ValidJsonObject_ReturnsCorrectCategoryList()
    {
        var json = @"{""categories"":
            [
                { ""value"": 0, ""label"": ""apple"" },
                { ""value"": 1, ""label"": ""orange"" },
                { ""value"": 2, ""label"": ""banana"" }
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<Category>());
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Object[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Object[2].Label, Is.EqualTo("banana"));
            Assert.That(((Category)wrapper.Object[0]).Value, Is.EqualTo(0));
            Assert.That(((Category)wrapper.Object[1]).Value, Is.EqualTo(1));
            Assert.That(((Category)wrapper.Object[2]).Value, Is.EqualTo(2));
        }
    }
}
