using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.OpenDataContract.Serialization.Yaml;

internal class DataContractNamingConvention : INamingConvention
{
    private Func<string, string> PropertyNameResolver { get; }
    private INamingConvention NamingConvention { get; }

    public DataContractNamingConvention()
    {
        NamingConvention = CamelCaseNamingConvention.Instance;
        PropertyNameResolver = value =>
        {
            return value;
        };
    }

    public string Apply(string value)
        => NamingConvention.Apply(PropertyNameResolver(value));

    public string Reverse(string value)
        => NamingConvention.Reverse(value);
}
