using Newtonsoft.Json;

namespace Packata.DataPackage.Serialization.Json;

internal sealed class CategoriesOrderedConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(bool?);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        => reader.TokenType == JsonToken.Boolean ? (bool)reader.Value!
            : throw new JsonSerializationException($"{reader.Path}: categoriesOrdered must be boolean when present.");

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) writer.WriteNull();
        else writer.WriteValue((bool)value);
    }
}
