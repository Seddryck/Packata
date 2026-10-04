using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Packata.DataPackage.Serialization.Json;
internal class ConstraintsConverter : JsonConverter
{
    private readonly ConstraintMapper _constraintMapper = new();

    public override bool CanConvert(Type objectType)
        => objectType == typeof(FieldConstraintCollection);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var list = new FieldConstraintCollection();

        var constraints = JObject.Load(reader);
        foreach (var property in constraints.Properties())
            list.Add(_constraintMapper.Map(property.Name, ToValue(property.Value)));

        return list;
    }

    private static object? ToValue(JToken token)
        => token switch
        {
            JValue value => value.Value,
            JArray array => array.Select(ToValue).ToArray(),
            JObject obj => obj.Properties().ToDictionary(property => property.Name, property => ToValue(property.Value)),
            _ => token.ToString()
        };

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
