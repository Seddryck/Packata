using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Newtonsoft.Json.Serialization;
using NUnit.Framework.Internal;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using DubUrl.Querying.Dialects.Casters;

namespace Packata.Core.Testing.Serialization.Json;

public abstract class AbstractConverterTests<T, U> where T : JsonConverter
{
    protected JsonSerializerSettings Settings { get; }

    public AbstractConverterTests(string propertyName)
    {
        Settings = new JsonSerializerSettings
        {
            Converters = [CreateConverter()],
            ContractResolver = new PropertyResolver(propertyName, CreateConverter())
        };
    }

    private class PropertyResolver : DefaultContractResolver
    {
        private Dictionary<string, JsonConverter> Converters { get; } = [];
        private Func<string, string> PropertyNameResolver { get; }
        private string PropertyName { get; }
        private JsonConverter Converter { get; }

        public PropertyResolver(string propertyName, JsonConverter converter)
        {
            PropertyName = propertyName;
            Converter = converter;
            Converters.Add(propertyName, converter);
            PropertyNameResolver = value =>
            {
                if (value == "Object")
                    return propertyName;
                return value.ToLowerInvariant();
            };
        }

        protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == PropertyName)
                property.Converter = Converter;
            return property;
        }

        protected override string ResolvePropertyName(string propertyName)
            => PropertyNameResolver(propertyName);
    }

    protected abstract T CreateConverter();

    protected class Wrapper
    {
        public U? Object { get; set; }
    }

    [Test]
    public void CanConvert_FieldType_ReturnsTrue()
    {
        var converter = CreateConverter();
        Assert.That(converter.CanConvert(typeof(U)), Is.True);
    }
}

public abstract class BaseConverterTests<T, U> : AbstractConverterTests<T, U> where T : JsonConverter, new()
{
    protected BaseConverterTests(string propertyName)
        : base(propertyName)
    { }

    protected override T CreateConverter()
        => new();
}
