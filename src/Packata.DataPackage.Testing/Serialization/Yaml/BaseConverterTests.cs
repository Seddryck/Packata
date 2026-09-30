using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Newtonsoft.Json.Serialization;
using NUnit.Framework.Internal;
using YamlDotNet.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.Core.Testing.Serialization.Yaml;

public abstract class AbstractConverterTests<T, U> where T : IYamlTypeConverter
{
    protected IDeserializer Deserializer { get; }

    protected AbstractConverterTests(string propertyName)
    {
        Deserializer = new DeserializerBuilder()
             .WithTypeConverter(CreateConverter())
             .WithNamingConvention(new CustomNamingConvention(propertyName))
             .Build();
    }

    public class CustomNamingConvention(string propertyName) : INamingConvention
    {
        public string Apply(string name)
        {
            if (name == "Object")
                return propertyName;
            return CamelCaseNamingConvention.Instance.Apply(name);
        }

        public string Reverse(string value)
            => throw new NotImplementedException();
    }

    protected abstract T CreateConverter();

    protected class Wrapper
    {
        public U? Object { get; set; }
    }

    [Test]
    public void Accepts_FieldType_ReturnsTrue()
    {
        var converter = CreateConverter();
        Assert.That(converter.Accepts(typeof(U)), Is.True);
    }
}

public abstract class BaseConverterTests<T, U> : AbstractConverterTests<T, U> where T : IYamlTypeConverter, new()
{
    protected BaseConverterTests(string propertyName)
        : base(propertyName)
    { }

    protected override T CreateConverter()
        => new();
}
