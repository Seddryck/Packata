using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class CategoriesConverterTests : BaseConverterTests<CategoriesConverter, List<ICategory>>
{
    public CategoriesConverterTests()
        : base("categories")
    { }

    [Test]
    public void ReadJson_ValidYamlArray_ReturnsCorrectPathList()
    {
        var yaml = @"
            categories:
              - apple
              - orange
              - banana
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<CategoryLabel>());
            Assert.That(wrapper.Object[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Object[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Object[2].Label, Is.EqualTo("banana"));
        }
    }

    [Test]
    public void ReadJson_ValidYamlValue_ReturnsCorrectPathList()
    {
        var yaml = @"
            categories:
              - value: 0
                label: apple
              - value: 1
                label: orange
              - value: 2
                label: banana
            ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<Category>());
            Assert.That(wrapper.Object[0].Label, Is.EqualTo("apple"));
            Assert.That(wrapper.Object[1].Label, Is.EqualTo("orange"));
            Assert.That(wrapper.Object[2].Label, Is.EqualTo("banana"));
            Assert.That(((Category)wrapper.Object[0]).Value, Is.EqualTo(0));
            Assert.That(((Category)wrapper.Object[1]).Value, Is.EqualTo(1));
            Assert.That(((Category)wrapper.Object[2]).Value, Is.EqualTo(2));
        }
    }
}
