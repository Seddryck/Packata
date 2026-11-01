using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Packata.DataPackage.Serialization.Json;

internal class CategoriesConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(List<ICategory>) || objectType == typeof(CategoryCollection);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var token = LoadToken(reader);
        if (token is JObject reference && objectType == typeof(CategoryCollection))
            return new CategoryCollection(CategoryMetadataConverter.ReadReference(reference));
        if (token is not JArray array) throw Invalid(token, "Expected an inline category array.");
        var result = ReadInline(array);
        return objectType == typeof(CategoryCollection) ? new CategoryCollection(result) : (object)result;
    }

    private static JToken LoadToken(JsonReader reader)
    {
        var dateHandling = reader.DateParseHandling;
        try
        {
            reader.DateParseHandling = DateParseHandling.None;
            return JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
        finally { reader.DateParseHandling = dateHandling; }
    }

    private static List<ICategory> ReadInline(JArray array)
    {
        var result = new List<ICategory>();
        bool? objects = null;
        foreach (var entry in array)
        {
            var objectForm = entry is JObject;
            if (objects is not null && objects != objectForm)
                throw Invalid(entry, "Categories must contain either scalar values or objects, not both.");
            objects = objectForm;
            result.Add(ReadCategory(entry));
        }
        return result;
    }

    private static ICategory ReadCategory(JToken entry)
    {
        if (entry is JObject obj)
        {
            var value = obj["value"] ?? throw Invalid(obj, "A category object requires value.");
            return new Category(ReadValue(value), ReadLabel(obj));
        }
        var scalar = ReadValue(entry);
        return scalar is string text ? new CategoryLabel(text) : new CategoryInteger((long)scalar);
    }

    private static string? ReadLabel(JObject obj)
    {
        if (!obj.TryGetValue("label", out var token)) return null;
        if (token.Type != JTokenType.String) throw Invalid(token, "A category label must be a string.");
        return token.Value<string>();
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
        if (value is CategoryCollection { Reference: not null } collection)
        { new CategoryMetadataConverter().WriteJson(writer, collection.Reference, serializer); return; }
        writer.WriteStartArray();
        foreach (var category in (IEnumerable<ICategory>)value)
        {
            if (category is Category)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("value"); serializer.Serialize(writer, category.Value);
                if (category.Label is not null)
                { writer.WritePropertyName("label"); writer.WriteValue(category.Label); }
                writer.WriteEndObject();
            }
            else
            {
                serializer.Serialize(writer, category.Value);
            }
        }
        writer.WriteEndArray();
    }
}
