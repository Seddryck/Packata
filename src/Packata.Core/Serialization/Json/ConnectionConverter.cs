using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.PathHandling;

namespace Packata.Core.Serialization.Json;
internal class ConnectionConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(IConnection).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
        {
            return new LiteralConnectionUrl((string)reader.Value!);
        }
        throw new JsonSerializationException("Unexpected JSON format.");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
