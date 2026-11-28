using System.Text.Json;
using Json.Pointer;
using Json.Schema;

namespace Packata.Ossie;

internal static class OssieDocumentValidation
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(OssieDocumentValidation).Assembly.GetManifestResourceStream("Packata.Ossie.Schema.ossie-schema.json")
            ?? throw new InvalidOperationException("The pinned Ossie schema is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    internal static void Validate(JsonElement document)
    {
        CheckDuplicateProperties(document, "$");
        if (document.ValueKind == JsonValueKind.Object
            && document.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString() != OssieModelSerializer.SupportedVersion)
        {
            throw new OssieDocumentException("$/version",
                $"Unsupported Ossie version '{version.GetString()}'. Expected '{OssieModelSerializer.SupportedVersion}'.");
        }

        var result = Schema.Value.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        if (!result.IsValid)
            throw SchemaError(result, document);
    }

    internal static string PropertyPath(string path, string name)
        => path + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static OssieDocumentException SchemaError(EvaluationResults result, JsonElement document)
    {
        var path = "$" + result.InstanceLocation;
        var errors = result.Errors;
        if (errors?.ContainsKey("anyOf") == true)
            return new OssieDocumentException(path, "An expression requires sql or dialects.");

        var value = result.InstanceLocation.Evaluate(document);
        if (errors?.TryGetValue("oneOf", out var unionMessage) == true)
        {
            // AI context is the schema's only oneOf. A failed object branch can
            // identify an invalid member more precisely than the union itself.
            var objectBranch = result.Details?.FirstOrDefault(child =>
                child.EvaluationPath.ToString().EndsWith("/oneOf/1", StringComparison.Ordinal));
            return value?.ValueKind == JsonValueKind.Object && objectBranch is not null
                ? SchemaError(objectBranch, document)
                : new OssieDocumentException(path, unionMessage);
        }

        var child = result.Details?.FirstOrDefault(detail => !detail.IsValid);
        if (child is not null)
            return SchemaError(child, document);

        if (errors?.ContainsKey("required") == true && value is { } obj
            && JsonPointer.TryParse(result.SchemaLocation.Fragment, out var pointer)
            && pointer.Evaluate(Schema.Value.Root.Source) is { } definition
            && definition.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                var name = property.GetString()!;
                if (!obj.TryGetProperty(name, out _))
                    return new OssieDocumentException(PropertyPath(path, name), "Required property is missing.");
            }
        }

        var message = errors?.Values.FirstOrDefault() ?? "The declaration does not satisfy the pinned Ossie schema.";
        return new OssieDocumentException(path, message);
    }

    private static void CheckDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = PropertyPath(path, property.Name);
                if (!names.Add(property.Name))
                    throw new OssieDocumentException(propertyPath, "Duplicate property.");
                CheckDuplicateProperties(property.Value, propertyPath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray())
                CheckDuplicateProperties(child, $"{path}/{index++}");
        }
    }
}
