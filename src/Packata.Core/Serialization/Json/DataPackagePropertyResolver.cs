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
    private readonly Dictionary<string, JsonConverter> _converters = new();
    private readonly Func<string, string> _propertyNameResolver;

    public DataPackagePropertyResolver(HttpClient httpClient, string root)
    {
        _converters.Add("path", new PathConverter(httpClient, root));
        _converters.Add("fields", new FieldConverter());
        _converters.Add("singleOrArray", new SingleOrArrayConverter());
        _propertyNameResolver = value =>
        {
            value = value.ToCamelCase();
            value = value == "profile" ? "$schema" : value;
            value = value == "paths" ? "path" : value;
            return value;
        };
    }

    protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (property.PropertyName == "path" && property.PropertyType == typeof(List<IPath>))
            property.Converter = _converters["path"];
        else if (property.PropertyName == "path" && property.PropertyType == typeof(List<string>))
            property.Converter = _converters["singleOrArray"];
        else if (property.PropertyName == "fields")
            property.Converter = _converters["fields"];
        return property;
    }

    protected override string ResolvePropertyName(string propertyName)
        => _propertyNameResolver(propertyName);
}
