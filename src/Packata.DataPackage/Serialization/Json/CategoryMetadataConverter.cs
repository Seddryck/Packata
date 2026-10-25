using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Packata.DataPackage.Serialization.Json;

internal sealed class CategoryMetadataConverter : JsonConverter
{
    public override bool CanConvert(Type type) => type == typeof(CategoryTableReference) || type == typeof(CategoryFieldMap);
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (token is not JObject obj) throw new JsonSerializationException($"{token.Path}: Expected a category metadata object.");
        return objectType == typeof(CategoryTableReference) ? ReadReference(obj) : ReadMap(obj);
    }
    internal static CategoryTableReference ReadReference(JObject obj) => new()
    {
        Resource = String(obj, "resource"),
        Package = String(obj, "package"),
        EncodedAs = String(obj, "encodedAs")
    };
    private static CategoryFieldMap ReadMap(JObject obj)
    {
        bool? ordered = null;
        if (obj.TryGetValue("ordered", out var token))
        {
            if (token.Type != JTokenType.Boolean) throw new JsonSerializationException($"{token.Path}: ordered must be boolean.");
            ordered = token.Value<bool>();
        }
        return new() { Value = String(obj, "value"), Label = String(obj, "label"), Ordered = ordered };
    }
    private static string? String(JObject obj, string name)
    {
        if (!obj.TryGetValue(name, out var token)) return null;
        if (token.Type != JTokenType.String) throw new JsonSerializationException($"{token.Path}: {name} must be a string.");
        return token.Value<string>();
    }
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        writer.WriteStartObject();
        if (value is CategoryTableReference reference)
        {
            Write(writer, "resource", reference.Resource);
            Write(writer, "package", reference.Package);
            Write(writer, "encodedAs", reference.EncodedAs);
        }
        else if (value is CategoryFieldMap map)
        {
            Write(writer, "value", map.Value); Write(writer, "label", map.Label);
            if (map.Ordered is not null) { writer.WritePropertyName("ordered"); writer.WriteValue(map.Ordered.Value); }
        }
        writer.WriteEndObject();
    }
    private static void Write(JsonWriter writer, string name, string? value)
    { if (value is not null) { writer.WritePropertyName(name); writer.WriteValue(value); } }
}
