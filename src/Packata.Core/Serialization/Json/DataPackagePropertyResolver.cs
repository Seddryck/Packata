using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Packata.Core.Storage;

namespace Packata.Core.Serialization.Json;
internal class DataPackagePropertyResolver : DefaultContractResolver
{
    private Dictionary<string, JsonConverter> Converters { get; } = [];
    private Func<string, string> PropertyNameResolver { get; }

    public DataPackagePropertyResolver(IDataPackageContainer container, IStorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(container);
        Converters.Add("resources", new ResourcesConverter(container.BaseUri?.ToString() ?? string.Empty));
        Converters.Add("path", new PathConverter(new PathFactory(container, provider)));
        Converters.Add("fields", new FieldConverter());
        Converters.Add("missingValues", new MissingValuesConverter());
        Converters.Add("constraints", new ConstraintsConverter());
        Converters.Add("categories", new CategoriesConverter());
        Converters.Add("connection", new ConnectionConverter());
        Converters.Add("dialect", new TableDialectConverter());
        PropertyNameResolver = value =>
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

        if (property.PropertyName == "resources" && property.PropertyType == typeof(List<Resource>))
            property.Converter = Converters["resources"];
        else if (property.PropertyName == "path" && property.PropertyType == typeof(List<IPath>))
            property.Converter = Converters["path"];
        else if (property.PropertyName == "path" && property.PropertyType == typeof(List<string>))
            property.Converter = new SingleOrArrayConverter();
        else if (property.PropertyName == "fields" && property.PropertyType == typeof(List<Field>))
            property.Converter = Converters["fields"];
        else if (property.PropertyName == "missingvalues")
            property.Converter = Converters["missingValues"];
        else if (property.PropertyName == "constraints")
            property.Converter = Converters["constraints"];
        else if (property.PropertyName == "categories")
            property.Converter = Converters["categories"];
        else if (property.PropertyName == "connection")
            property.Converter = Converters["connection"];
        else if (property.PropertyName == "dialect")
            property.Converter = Converters["dialect"];
        return property;
    }

    protected override string ResolvePropertyName(string propertyName)
        => PropertyNameResolver(propertyName);
}
