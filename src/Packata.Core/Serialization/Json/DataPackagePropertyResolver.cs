using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Serialization.Json;
internal class DataPackagePropertyResolver : DefaultContractResolver
{
    private readonly JsonConverter _converter;
    private readonly Func<string, string> _propertyNameResolver;

    public DataPackagePropertyResolver(HttpClient httpClient, string root)
    {
        _converter = new PathConverter(httpClient, root);
        _propertyNameResolver = value =>
        {
            value = value.ToLowerInvariant();
            value = value == "profile" ? "$schema" : value;
            value = value == "paths" ? "path" : value;
            return value;
        };
    }

    protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (property.PropertyName == "path" && property.PropertyType == typeof(List<IPath>))
            property.Converter = _converter;
        else if (property.PropertyName == "path" && property.PropertyType == typeof(List<string>))
            property.Converter = new SingleOrArrayConverter();
        return property;
    }

    protected override string ResolvePropertyName(string propertyName)
        => _propertyNameResolver(propertyName);
}
