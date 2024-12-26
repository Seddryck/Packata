using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Serialization.Json;
internal class DataPackagePropertyResolver : DefaultContractResolver
{
    private readonly Func<string, string> _propertyNameResolver;

    public DataPackagePropertyResolver()
        => _propertyNameResolver = value =>
        {
            value = value.ToLowerInvariant();
            value = value == "profile" ? "$schema" : value;
            return value;
        };

    protected override string ResolvePropertyName(string propertyName)
        => _propertyNameResolver(propertyName);
}
