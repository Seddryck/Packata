using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Packata.Core.Serialization.Json;
internal class CategoriesConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => objectType == typeof(List<ICategory>);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException("Unexpected JSON format.");

        var list = new List<ICategory>();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            if (reader.TokenType == JsonToken.String)
                list.Add(new CategoryLabel((string)reader.Value!));
            else if (reader.TokenType == JsonToken.StartObject)
            {
                var category = serializer.Deserialize<Category>(reader)!;
                list.Add(category);
            }
        }
        return list;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
