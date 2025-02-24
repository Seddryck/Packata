using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;
using Packata.Core.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Tests.Serialization.Json
{
    public class PathConverterTests
    {
        private readonly JsonSerializerSettings _settings;

        public PathConverterTests()
        {
            _settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new PathConverter(new HttpClient(), "c:\\") },
                ContractResolver = new FieldsPropertyResolver()
            };
        }

        private class FieldsPropertyResolver : DefaultContractResolver
        {
            private readonly Dictionary<string, JsonConverter> _converters = new();
            private readonly Func<string, string> _propertyNameResolver;

            public FieldsPropertyResolver()
            {
                _converters.Add("path", new PathConverter(new HttpClient(), "c:\\"));
                _propertyNameResolver = value =>
                {
                    return value.ToLowerInvariant();
                };
            }

            protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                if (property.PropertyName == "path" && property.PropertyType == typeof(List<IPath>))
                    property.Converter = new PathConverter(new HttpClient(), "c:\\");
                return property;
            }

            protected override string ResolvePropertyName(string propertyName)
                => _propertyNameResolver(propertyName);
        }

        private class PathCollectionWrapper
        {
            public List<IPath>? Path { get; set; }
        }

        [Test]
        public void CanConvert_FieldType_ReturnsTrue()
        {
            var converter = new SingleOrArrayConverter();
            Assert.That(converter.CanConvert(typeof(List<IPath>)), Is.True);
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
            Assert.That(wrapper.Path[0], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Path[1], Is.InstanceOf<IPath>());
            Assert.That(wrapper.Path[2], Is.InstanceOf<IPath>());
        }

        [Test]
        public void ReadJson_ValidJsonValue_ReturnsCorrectFieldList()
        {
            var json = @"{""path"": ""path_01""}";

            var wrapper = JsonConvert.DeserializeObject<PathCollectionWrapper>(json, _settings);

            Assert.That(wrapper?.Path, Is.Not.Null);
            Assert.That(wrapper.Path, Has.Count.EqualTo(1));
            Assert.That(wrapper.Path[0], Is.InstanceOf<IPath>());
        }
    }
}
