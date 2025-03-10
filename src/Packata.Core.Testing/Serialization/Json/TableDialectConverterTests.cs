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

public class TableDialectConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public TableDialectConverterTests()
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
            _converters.Add("dialect", new TableDialectConverter());
            _propertyNameResolver = value =>
            {
                return value.ToLowerInvariant();
            };
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == "dialect" && property.PropertyType == typeof(TableDialect))
                property.Converter = new TableDialectConverter();
            return property;
        }

        protected override string ResolvePropertyName(string propertyName)
            => _propertyNameResolver(propertyName);
    }

    private class TableDialectWrapper
    {
        public TableDialect? Dialect { get; set; }
    }

    [Test]
    public void CanConvert_FieldType_ReturnsTrue()
    {
        var converter = new TableDialectConverter();
        Assert.That(converter.CanConvert(typeof(TableDialect)), Is.True);
    }

    [Test]
    public void ReadJson_TypeDelimited_ReturnsDelimited()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""type"": ""delimited"",
                ""delimiter"": "";""
            }}";
        var wrapper = JsonConvert.DeserializeObject<TableDialectWrapper>(json, _settings);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDelimitedDialect>());
        var delimitedDialect = (TableDelimitedDialect)wrapper.Dialect;
        Assert.That(delimitedDialect.Delimiter, Is.EqualTo(';'));
    }

    [Test]
    public void ReadJson_TypeMissing_ReturnsDelimited()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""delimiter"": "";""
            }}";
        var wrapper = JsonConvert.DeserializeObject<TableDialectWrapper>(json, _settings);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDelimitedDialect>());
        var delimitedDialect = (TableDelimitedDialect)wrapper.Dialect;
        Assert.That(delimitedDialect.Delimiter, Is.EqualTo(';'));
    }

    [Test]
    public void ReadJson_TypeDatabase_ReturnsDatabase()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""type"": ""database"",
                ""table"": ""Customer""
            }}";
        var wrapper = JsonConvert.DeserializeObject<TableDialectWrapper>(json, _settings);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDatabaseDialect>());
        var dbDialect = (TableDatabaseDialect)wrapper.Dialect;
        Assert.That(dbDialect.Table, Is.EqualTo("Customer"));
    }
}
