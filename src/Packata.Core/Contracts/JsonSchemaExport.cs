using System.Text.Json;

namespace Packata.Core.Contracts;

/// <summary>A JSON Schema Draft 2020-12 document suitable for JSON serialization.</summary>
public sealed class JsonSchemaDocument : Dictionary<string, object?>
{
    public void Serialize(Stream stream) => JsonSerializer.Serialize(stream, this,
        new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Converts a canonical asset to a schema for one dataset record.</summary>
public static class JsonSchemaExport
{
    public static JsonSchemaDocument ToJsonSchema(this DataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Schema is null) throw new JsonSchemaConversionException("The dataset has no schema.");
        var result = new JsonSchemaDocument
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = asset.Name,
            ["type"] = "object"
        };
        if (asset.Description is not null) result["description"] = asset.Description;
        AddProperties(result, asset.Schema.Fields, asset.Name);
        return result;
    }

    private static void AddProperties(Dictionary<string, object?> schema, IReadOnlyList<DataField> fields, string path)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!properties.TryAdd(field.Name, ConvertField(field, path + "." + field.Name)))
                throw new JsonSchemaConversionException($"Duplicate property at '{path}.{field.Name}'.");
        }
        schema["properties"] = properties;
        var required = fields.Where(field => field.Required).Select(field => field.Name).ToArray();
        if (required.Length > 0) schema["required"] = required;
    }

    private static Dictionary<string, object?> ConvertField(DataField field, string path)
    {
        var type = field.LogicalType?.ToLowerInvariant() switch
        {
            "string" or "date" or "datetime" or "timestamp" or "time" => "string",
            "boolean" => "boolean",
            "integer" => "integer",
            "number" or "decimal" => "number",
            "array" => "array",
            "object" => "object",
            _ => throw new JsonSchemaConversionException($"Unsupported logical type at '{path}'.")
        };
        var result = new Dictionary<string, object?>
        {
            ["type"] = field.Nullable ? new[] { type, "null" } : (object)type
        };
        if (field.Description is not null) result["description"] = field.Description;
        var format = field.LogicalType?.ToLowerInvariant() switch
        {
            "date" => "date",
            "datetime" or "timestamp" => "date-time",
            "time" => "time",
            _ => field.Format is "default" or "" ? null : field.Format
        };
        if (format is not null) result["format"] = format;
        if (type == "object") AddProperties(result, field.Children, path);
        else if (field.Children.Count > 0)
            throw new JsonSchemaConversionException($"Nested properties require an object at '{path}'.");
        if (field.Items is not null)
        {
            if (type != "array") throw new JsonSchemaConversionException($"Items require an array at '{path}'.");
            result["items"] = ConvertField(field.Items, path + "[]");
        }
        foreach (var constraint in field.Constraints)
            AddConstraint(result, constraint, type, path);
        return result;
    }

    private static void AddConstraint(Dictionary<string, object?> result, DataConstraint constraint, string type, string path)
    {
        var keyword = constraint.Kind;
        var value = JsonSerializer.SerializeToElement(constraint.Value);
        var valid = keyword switch
        {
            "minLength" or "maxLength" => type == "string" && NonNegativeInteger(value),
            "pattern" => type == "string" && value.ValueKind == JsonValueKind.String,
            "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" =>
                type is "integer" or "number" && value.ValueKind == JsonValueKind.Number,
            "multipleOf" => type is "integer" or "number" && value.ValueKind == JsonValueKind.Number && value.GetDouble() > 0,
            "minItems" or "maxItems" => type == "array" && NonNegativeInteger(value),
            "uniqueItems" => type == "array" && value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "minProperties" or "maxProperties" => type == "object" && NonNegativeInteger(value),
            "enum" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 &&
                value.EnumerateArray().Select(item => item.GetRawText()).Distinct().Count() == value.GetArrayLength(),
            "required" => type == "object" && value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            // These describe dataset-wide semantics, which a schema for a single record cannot enforce.
            "unique" or "primaryKey" when value.ValueKind == JsonValueKind.False => true,
            _ => false
        };
        if (!valid) throw new JsonSchemaConversionException($"Unsupported or invalid constraint '{keyword}' at '{path}'.");
        if (keyword is "unique" or "primaryKey") return;
        if (keyword == "required")
        {
            var existing = result.TryGetValue("required", out var names) ? (string[])names! : [];
            result[keyword] = existing.Concat(value.EnumerateArray().Select(item => item.GetString()!))
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        else if (!result.TryAdd(keyword, constraint.Value))
            throw new JsonSchemaConversionException($"Duplicate constraint '{keyword}' at '{path}'.");
    }

    private static bool NonNegativeInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0;
}

public sealed class JsonSchemaConversionException(string message) : InvalidOperationException(message);
