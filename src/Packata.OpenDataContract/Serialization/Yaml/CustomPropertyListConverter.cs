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
        var deserializer = new DeserializerBuilder().Build();
        var list = deserializer.Deserialize<List<Dictionary<string, object>>>(parser);

        var dict = new CustomProperties();
        foreach (var item in list)
        {
            if (item.TryGetValue("property", out var keyObj) &&
                item.TryGetValue("value", out var valObj) &&
                keyObj is string key)
            {
                dict[key] = valObj;
            }
        }

        return dict;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotImplementedException("Serialization not supported");
    }
}
