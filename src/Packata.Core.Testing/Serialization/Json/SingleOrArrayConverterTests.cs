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

public class SingleOrArrayConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public SingleOrArrayConverterTests()
    {
        _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new SingleOrArrayConverter() },
            ContractResolver = new FieldsPropertyResolver()
        };
    }

    private class FieldsPropertyResolver : DefaultContractResolver
    {
        private readonly Dictionary<string, JsonConverter> _converters = new();
        private readonly Func<string, string> _propertyNameResolver;

        public FieldsPropertyResolver()
        {
            _converters.Add("fields", new FieldConverter());
            _propertyNameResolver = value =>
            {
                return value.ToLowerInvariant();
            };
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == "path" && property.PropertyType == typeof(List<string>))
                property.Converter = new SingleOrArrayConverter();
            return property;
        }

        protected override string ResolvePropertyName(string propertyName)
            => _propertyNameResolver(propertyName);
    }

    private class PathCollectionWrapper
    {
        public List<string>? Path { get; set; }
    }

    [Test]
    public void CanConvert_FieldType_ReturnsTrue()
    {
        var converter = new SingleOrArrayConverter();
        Assert.That(converter.CanConvert(typeof(List<string>)), Is.True);
    }

    [Test]
    public void ReadJson_ValidJsonArray_ReturnsCorrectFieldList()
    {
        var json = @"{""path"":
            [
                ""path_01"",
                ""path_02"",
                ""path_03""
            ]}";

        var wrapper = JsonConvert.DeserializeObject<PathCollectionWrapper>(json, _settings);

        Assert.That(wrapper?.Path, Is.Not.Null);
        Assert.That(wrapper.Path, Has.Count.EqualTo(3));
        Assert.That(wrapper.Path[0], Is.EqualTo("path_01"));
        Assert.That(wrapper.Path[1], Is.EqualTo("path_02"));
        Assert.That(wrapper.Path[2], Is.EqualTo("path_03"));
    }


    [Test]
    public void ReadJson_ValidJsonValue_ReturnsCorrectFieldList()
    {
        var json = @"{""path"": ""path_01""}";

        var wrapper = JsonConvert.DeserializeObject<PathCollectionWrapper>(json, _settings);

        Assert.That(wrapper?.Path, Is.Not.Null);
        Assert.That(wrapper.Path, Has.Count.EqualTo(1));
        Assert.That(wrapper.Path[0], Is.EqualTo("path_01"));
    }
}
