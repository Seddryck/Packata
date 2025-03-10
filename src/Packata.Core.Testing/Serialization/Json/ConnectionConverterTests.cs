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

public class ConnectionConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public ConnectionConverterTests()
    {
        _settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new ConnectionConverter() },
            ContractResolver = new FieldsPropertyResolver()
        };
    }

    private class FieldsPropertyResolver : DefaultContractResolver
    {
        private readonly Dictionary<string, JsonConverter> _converters = new();
        private readonly Func<string, string> _propertyNameResolver;

        public FieldsPropertyResolver()
        {
            _converters.Add("connection", new ConnectionConverter());
            _propertyNameResolver = value =>
            {
                return value.ToLowerInvariant();
            };
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == "connection" && property.PropertyType == typeof(IConnection))
                property.Converter = new ConnectionConverter();
            return property;
        }

        protected override string ResolvePropertyName(string propertyName)
            => _propertyNameResolver(propertyName);
    }

    private class ConnectionWrapper
    {
        public IConnection? Connection { get; set; }
    }

    [Test]
    public void CanConvert_FieldType_ReturnsTrue()
    {
        var converter = new ConnectionConverter();
        Assert.That(converter.CanConvert(typeof(IConnection)), Is.True);
    }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectConnectionUrl()
    {
        var json = @"{""connection"": ""mssql://server/db""}";

        var wrapper = JsonConvert.DeserializeObject<ConnectionWrapper>(json, _settings);

        Assert.That(wrapper?.Connection, Is.Not.Null);
        Assert.That(wrapper.Connection, Is.TypeOf<LiteralConnectionUrl>());
        Assert.That(wrapper.Connection.ConnectionUrl, Is.EqualTo("mssql://server/db"));
    }
}
