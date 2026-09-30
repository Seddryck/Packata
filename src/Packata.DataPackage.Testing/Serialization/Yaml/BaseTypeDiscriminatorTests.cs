using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Packata.Core.Serialization.Yaml;

namespace Packata.Core.Testing.Serialization.Yaml;

internal abstract class BaseTypeDiscriminatorTests<T, U> where T : ITypeDiscriminator, new()
{
    protected IDeserializer Deserializer { get; }

    protected BaseTypeDiscriminatorTests(string propertyName)
    {
        Deserializer = new DeserializerBuilder()
             .WithNamingConvention(new CustomNamingConvention(propertyName))
             .WithTypeDiscriminatingNodeDeserializer((o) => new T().Execute(o))
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

    protected class Wrapper
    {
        public U? Object { get; set; }
    }
}
