using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Packata.Core.Serialization.Json;
internal class SingleOrArrayConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => objectType == typeof(List<string>);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return new List<string>();

        if (reader.TokenType == JsonToken.String)
            return new List<string> { (string)reader.Value! };

        if (reader.TokenType == JsonToken.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;

                if (reader.TokenType == JsonToken.String)
                    list.Add((string)reader.Value!);
            }
            return list;
        }

        throw new JsonSerializationException("Unexpected JSON format.");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
