using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Packata.Core.Serialization.Yaml;
using YamlDotNet.Serialization.ObjectFactories;

namespace Packata.Core.Testing.Serialization.Yaml;

internal abstract class BaseObjectFactoryTests<T, U> where T : DefaultObjectFactory
{
    protected IDeserializer Deserializer { get; }

    protected BaseObjectFactoryTests(string propertyName)
    {
        Deserializer = new DeserializerBuilder()
             .WithNamingConvention(new CustomNamingConvention(propertyName))
             .WithObjectFactory(CreateObjectFactory())
             .Build();
    }

    protected abstract T CreateObjectFactory();

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
