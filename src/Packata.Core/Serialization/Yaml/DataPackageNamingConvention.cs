using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.Core.Serialization.Yaml
{
    internal class DataPackageNamingConvention : INamingConvention
    {
        private readonly Func<string, string> _propertyNameResolver;
        private readonly INamingConvention _namingConvention;

        public DataPackageNamingConvention()
        {
            _namingConvention = CamelCaseNamingConvention.Instance;
            _propertyNameResolver = value =>
            {
                value = value == "Profile" ? "$schema" : value;
                value = value == "Paths" ? "path" : value;
                return value;
            };
        }

        public string Apply(string value)
            => _namingConvention.Apply(_propertyNameResolver(value));

        public string Reverse(string value)
            => _namingConvention.Reverse(value);
    }
}
