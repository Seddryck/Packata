using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Packata.DataPackage.Serialization.Json;

internal class CategoriesConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(List<ICategory>);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader);
        if (token is not JArray array) throw Invalid(token, "Expected an inline category array.");
        var result = new List<ICategory>();
        bool? objects = null;
        foreach (var entry in array)
        {
            var isObject = entry is JObject;
            if (objects is not null && objects != isObject)
                throw Invalid(entry, "Categories must contain either scalar values or objects, not both.");
            objects = isObject;
            if (entry is JObject obj)
            {
                var value = obj["value"] ?? throw Invalid(obj, "A category object requires value.");
                string? label = null;
                if (obj.TryGetValue("label", out var labelToken))
                {
                    if (labelToken.Type != JTokenType.String) throw Invalid(labelToken, "A category label must be a string.");
                    label = labelToken.Value<string>();
                }
                result.Add(new Category(ReadValue(value), label));
            }
            else
            {
                var value = ReadValue(entry);
                result.Add(value is string text ? new CategoryLabel(text) : new CategoryInteger((long)value));
            }
        }
        return result;
    }

    private static object ReadValue(JToken token)
    {
        if (token is JValue scalar && token.Type is JTokenType.String or JTokenType.Integer &&
            CategoryValues.TryNormalize(scalar.Value, out var value)) return value!;
        throw Invalid(token, "A category value must be a string or a signed 64-bit integer.");
    }

    private static JsonSerializationException Invalid(JToken token, string message)
        => new($"{token.Path}: {message}");

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        writer.WriteStartArray();
        foreach (var category in (List<ICategory>)value)
        {
            if (category is Category)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("value"); serializer.Serialize(writer, category.Value);
                if (category.Label is not null)
                { writer.WritePropertyName("label"); writer.WriteValue(category.Label); }
                writer.WriteEndObject();
            }
            else serializer.Serialize(writer, category.Value);
        }
        writer.WriteEndArray();
    }
}
