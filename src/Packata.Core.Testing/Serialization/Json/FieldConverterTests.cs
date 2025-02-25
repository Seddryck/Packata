using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;
using Packata.Core.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Testing.Serialization.Json
{
    public class FieldConverterTests
    {
        private readonly JsonSerializerSettings _settings;

        public FieldConverterTests()
        {
            _settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new FieldConverter() },
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

                if (property.PropertyName == "fields" && property.PropertyType == typeof(List<Field>))
                    property.Converter = _converters["fields"];
                return property;
            }

            protected override string ResolvePropertyName(string propertyName)
        => _propertyNameResolver(propertyName);
        }

        private class FieldCollectionWrapper
        {
            public List<Field>? Fields { get; set; }
        }

        [Test]
        public void CanConvert_FieldType_ReturnsTrue()
        {
            var converter = new FieldConverter();
            Assert.That(converter.CanConvert(typeof(List<Field>)), Is.True);
        }

        [Test]
        public void ReadJson_ValidJson_ReturnsCorrectFieldList()
        {
            var json = @"{""fields"":
            [
                { ""type"": ""string"", ""name"": ""test"" },
                { ""type"": ""number"", ""name"": 123 },
                { ""type"": ""boolean"",""name"": true }
            ]}";

            var wrapper = JsonConvert.DeserializeObject<FieldCollectionWrapper>(json, _settings);

            Assert.That(wrapper!.Fields, Is.Not.Null);
            Assert.That(wrapper.Fields.Count, Is.EqualTo(3));
            Assert.That(wrapper.Fields[0], Is.TypeOf<StringField>());
            Assert.That(wrapper.Fields[1], Is.TypeOf<NumberField>());
            Assert.That(wrapper.Fields[2], Is.TypeOf<BooleanField>());
        }
    }
}
