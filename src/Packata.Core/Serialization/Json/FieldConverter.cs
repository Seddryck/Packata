using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;

namespace Packata.Core.Serialization.Json;
internal class FieldConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(Field).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var fields = JArray.Load(reader);
        var list = new List<Field>();
        foreach (var field in fields)
        {
            var type = ((JObject)field)["type"]?.Value<string>() ?? string.Empty;

            list.Add(type switch
            {
                "string" => ((JObject)field).ToObject<StringField>(serializer)!,
                "number" => ((JObject)field).ToObject<NumberField>(serializer)!,
                "integer" => ((JObject)field).ToObject<IntegerField>(serializer)!,
                "date" => ((JObject)field).ToObject<DateField>(serializer)!,
                "time" => ((JObject)field).ToObject<TimeField>(serializer)!,
                "datetime" => ((JObject)field).ToObject<DateTimeField>(serializer)!,
                "year" => ((JObject)field).ToObject<YearField>(serializer)!,
                "yearmonth" => ((JObject)field).ToObject<YearMonthField>(serializer)!,
                "boolean" => ((JObject)field).ToObject<BooleanField>(serializer)!,
                "object" => ((JObject)field).ToObject<ObjectField>(serializer)!,
                _ => ((JObject)field).ToObject<Field>(serializer)!,
            });
        }
        return list;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is StringField stringField)
        {
            serializer.Serialize(writer, stringField);
        }
        else if (value is NumberField numberField)
        {
            serializer.Serialize(writer, numberField);
        }
        else
        {
            throw new JsonSerializationException("Unknown Field type.");
        }
    }
}
