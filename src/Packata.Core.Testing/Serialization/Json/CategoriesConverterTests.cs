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

public class CategoriesConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public CategoriesConverterTests()
    {
        _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new CategoriesConverter() },
            ContractResolver = new CategoriesPropertyResolver()
        };
    }

    private class CategoriesPropertyResolver : DefaultContractResolver
    {
        private readonly Dictionary<string, JsonConverter> _converters = new();
        private readonly Func<string, string> _propertyNameResolver;

        public CategoriesPropertyResolver()
        {
            _converters.Add("categories", new CategoriesConverter());
            _propertyNameResolver = value =>
            {
                return value.ToLowerInvariant();
            };
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == "categories")
                property.Converter = new CategoriesConverter();
            return property;
        }

        protected override string ResolvePropertyName(string propertyName)
            => _propertyNameResolver(propertyName);
    }

    private class CategoriesWrapper
    {
        public List<ICategory>? Categories { get; set; }
    }

    [Test]
    public void CanConvert_ListICategory_ReturnsTrue()
    {
        var converter = new CategoriesConverter();
        Assert.That(converter.CanConvert(typeof(List<ICategory>)), Is.True);
    }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectCategoryList()
    {
        var json = @"{""categories"":
            [
                ""apple"",
                ""orange"",
                ""banana""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<CategoriesWrapper>(json, _settings);

        Assert.That(wrapper?.Categories, Is.Not.Null);
        Assert.That(wrapper.Categories, Has.Count.EqualTo(3));
        Assert.That(wrapper.Categories, Is.All.InstanceOf<CategoryLabel>());
        Assert.That(wrapper.Categories[0].Label, Is.EqualTo("apple"));
        Assert.That(wrapper.Categories[1].Label, Is.EqualTo("orange"));
        Assert.That(wrapper.Categories[2].Label, Is.EqualTo("banana"));
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

        var wrapper = JsonConvert.DeserializeObject<CategoriesWrapper>(json, _settings);

        Assert.That(wrapper?.Categories, Is.Not.Null);
        Assert.That(wrapper.Categories, Has.Count.EqualTo(3));
        Assert.That(wrapper.Categories, Is.All.InstanceOf<Category>());
        Assert.That(wrapper.Categories[0].Label, Is.EqualTo("apple"));
        Assert.That(wrapper.Categories[1].Label, Is.EqualTo("orange"));
        Assert.That(wrapper.Categories[2].Label, Is.EqualTo("banana"));
        Assert.That(((Category)wrapper.Categories[0]).Value, Is.EqualTo(0));
        Assert.That(((Category)wrapper.Categories[1]).Value, Is.EqualTo(1));
        Assert.That(((Category)wrapper.Categories[2]).Value, Is.EqualTo(2));
    }
}
