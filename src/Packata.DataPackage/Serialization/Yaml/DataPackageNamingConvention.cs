using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.Core.Serialization.Yaml;

internal class DataPackageNamingConvention : INamingConvention
{
    private Func<string, string> PropertyNameResolver { get; }
    private INamingConvention NamingConvention { get; }

    public DataPackageNamingConvention()
    {
        NamingConvention = CamelCaseNamingConvention.Instance;
        PropertyNameResolver = value =>
        {
            value = value == "Profile" ? "$schema" : value;
            value = value == "Paths" ? "path" : value;
            return value;
        };
    }

    public string Apply(string value)
        => NamingConvention.Apply(PropertyNameResolver(value));

    public string Reverse(string value)
        => NamingConvention.Reverse(value);
}
