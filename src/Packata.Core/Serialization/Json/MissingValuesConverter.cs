using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Packata.Core.Serialization.Json;
internal class MissingValuesConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(List<MissingValue>).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var result = new List<MissingValue>();

        if (reader.TokenType == JsonToken.StartArray)
        {
            // Read the array
            var array = JArray.Load(reader);

            foreach (var item in array)
            {
                if (item.Type == JTokenType.Object)
                {
                    // Object format: { "value": "", "label": "missing" }
                    result.Add(item.ToObject<MissingValue>(serializer)!);
                }
                else if (item.Type == JTokenType.String)
                {
                    // String format: "NaN"
                    result.Add(new MissingValue(item.ToString(), null));
                }
            }
        }

        return result;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var list = (List<MissingValue>)value!;

        if (list == null || list.Count == 0)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        // Determine if the output should be in object or string format
        var isSimpleFormat = list.TrueForAll(v => string.IsNullOrEmpty(v.Label));

        writer.WriteStartArray();

        foreach (var missingValue in list)
        {
            if (isSimpleFormat)
            {
                // Write as strings
                writer.WriteValue(missingValue.Value);
            }
            else
            {
                // Write as objects
                serializer.Serialize(writer, missingValue);
            }
        }

        writer.WriteEndArray();
    }
}
