using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.PathHandling;

namespace Packata.Core.Serialization.Json;
internal class PathConverter : JsonConverter
{
    private HttpClient _httpClient;
    private string _root;

    public PathConverter(HttpClient httpClient, string root)
        => (_httpClient, _root) = (httpClient, root);

    public override bool CanConvert(Type objectType)
        => typeof(List<IPath>).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return new List<IPath>();

        if (reader.TokenType == JsonToken.String)
        {
            return new List<IPath>()
            {
                BuildPath((string)reader.Value!)
            };
        }

        if (reader.TokenType == JsonToken.StartArray)
        {
            var list = new List<IPath>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;

                if (reader.TokenType == JsonToken.String)
                    list.Add(BuildPath((string)reader.Value!));
            }
            return list;
        }

        throw new JsonSerializationException("Unexpected JSON format.");
    }

    private IPath BuildPath(string value)
        => value.Contains("://") ? new HttpPath(_httpClient, value) : new LocalPath(_root, value);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
