using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.Core.Serialization.Yaml
{
    internal class DataPackagePropertyResolver : INamingConvention
    {
        private readonly Dictionary<string, IYamlTypeConverter> _converters = new();
        private readonly Func<string, string> _propertyNameResolver;

        public DataPackagePropertyResolver(HttpClient httpClient, string root)
        {
            _converters.Add("path", new PathConverter(httpClient, root));
            _converters.Add("fields", new FieldConverter());
            _converters.Add("missingValues", new MissingValuesConverter());
            _propertyNameResolver = value =>
            {
                value = value.ToLowerInvariant();
                value = value == "profile" ? "$schema" : value;
                value = value == "paths" ? "path" : value;
                return value;
            };
        }

        public string Apply(string value)
            => _propertyNameResolver(value);

        public string Reverse(string value)
            => throw new NotImplementedException();
    }
}
