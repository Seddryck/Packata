using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;

namespace Packata.Ossie;

/// <summary>Reads and writes one standalone Ossie Layer 1 model.</summary>
public static class OssieModelSerializer
{
    public const string SupportedVersion = "0.3.0.dev0";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter<OssieDataType>(allowIntegerValues: false), new OssieAiContextConverter() },
    };

    /// <summary>Reads one model and leaves the input stream open.</summary>
    public static OssieModel Deserialize(Stream stream, OssieSerializationFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var document = ReadDocument(reader, format);
        OssieDocumentValidation.Validate(document.RootElement);
        var model = document.RootElement.Deserialize<OssieModel>(Options)!;
        OssieDeclarationValidation.Validate(model);
        return model;
    }

    /// <summary>Validates and writes one model and leaves the output stream open.</summary>
    public static void Serialize(OssieModel model, Stream stream, OssieSerializationFormat format)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stream);
        if (format is not (OssieSerializationFormat.Json or OssieSerializationFormat.Yaml))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Expected JSON or YAML.");

        var document = ValidateModel(model);

        if (format == OssieSerializationFormat.Json)
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            document.WriteTo(writer);
        }
        else
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            OssieYaml.Write(document, writer);
        }
    }

    internal static JsonElement ValidateModel(OssieModel model)
    {
        var document = JsonSerializer.SerializeToElement(model, Options);
        OssieDocumentValidation.Validate(document);
        OssieDeclarationValidation.Validate(model);
        return document;
    }

    private static JsonDocument ReadDocument(TextReader reader, OssieSerializationFormat format)
    {
        try
        {
            return format switch
            {
                OssieSerializationFormat.Json => JsonDocument.Parse(reader.ReadToEnd()),
                OssieSerializationFormat.Yaml => OssieYaml.Read(reader),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Expected JSON or YAML."),
            };
        }
        catch (JsonException exception)
        {
            throw new OssieDocumentException("$", exception.Message, exception);
        }
        catch (YamlException exception)
        {
            throw new OssieDocumentException("$", $"YAML at {exception.Start}: {exception.Message}", exception);
        }
    }
}
