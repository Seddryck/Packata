using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;
public class CustomPropertyListConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) =>
        type == typeof(CustomProperties);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var list = rootDeserializer(typeof(List<CustomProperty>)) as List<CustomProperty>;

        if (list == null)
            return new CustomProperties();

        var properties = new CustomProperties();
        properties.AddRange(list);
        return properties;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var properties = value as CustomProperties ?? [];
        serializer(properties.ToList());
    }
}
