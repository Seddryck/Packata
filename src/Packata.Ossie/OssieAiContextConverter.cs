using System.Text.Json;
using System.Text.Json.Serialization;

namespace Packata.Ossie;

internal sealed class OssieAiContextConverter : JsonConverter<OssieAiContext>
{
    public override OssieAiContext Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => new OssieAiContext(reader.GetString()!),
            JsonTokenType.StartObject => new OssieAiContext(JsonSerializer.Deserialize<OssieAiContextDetails>(ref reader, options)!),
            _ => throw new JsonException("Expected a string or an AI context object."),
        };

    public override void Write(Utf8JsonWriter writer, OssieAiContext value, JsonSerializerOptions options)
    {
        if (value.Text is { } text)
            writer.WriteStringValue(text);
        else
            JsonSerializer.Serialize(writer, value.Details, options);
    }
}
