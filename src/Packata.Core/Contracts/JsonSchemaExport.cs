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
        result["additionalProperties"] = asset.Schema.AllowAdditionalProperties;
        if (NativeSchemaRequirements.RequiresOverlap(asset.Schema))
            result["anyOf"] = asset.Schema.Fields.Select(field =>
                new Dictionary<string, object?> { ["required"] = new[] { field.Name } }).ToArray();
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
        var type = GetJsonType(field.LogicalType, path);
        var result = new Dictionary<string, object?>
        {
            ["type"] = field.Nullable ? new[] { type, "null" } : (object)type
        };
        if (field.Description is not null) result["description"] = field.Description;
        var format = GetJsonFormat(field);
        if (format is not null) result["format"] = format;
        AddNestedSchemas(result, field, type, path);
        foreach (var constraint in field.Constraints)
            AddConstraint(result, constraint, type, path);
        return result;
    }

    private static string GetJsonType(string? logicalType, string path) => logicalType?.ToLowerInvariant() switch
    {
        "string" or "date" or "datetime" or "timestamp" or "time" => "string",
        "boolean" => "boolean",
        "integer" => "integer",
        "number" or "decimal" => "number",
        "array" => "array",
        "object" => "object",
        _ => throw new JsonSchemaConversionException($"Unsupported logical type at '{path}'.")
    };

    private static string? GetJsonFormat(DataField field) => field.LogicalType?.ToLowerInvariant() switch
    {
        "date" => "date",
        "datetime" or "timestamp" => "date-time",
        "time" => "time",
        _ => field.Format is "default" or "" ? null : field.Format
    };

    private static void AddNestedSchemas(Dictionary<string, object?> result, DataField field, string type, string path)
    {
        if (type == "object") AddProperties(result, field.Children, path);
        else if (field.Children.Count > 0)
            throw new JsonSchemaConversionException($"Nested properties require an object at '{path}'.");
        if (field.Items is not null)
        {
            if (type != "array") throw new JsonSchemaConversionException($"Items require an array at '{path}'.");
            result["items"] = ConvertField(field.Items, path + "[]");
        }
    }

    private static void AddConstraint(Dictionary<string, object?> result, DataConstraint constraint, string type, string path)
    {
        var keyword = constraint.Kind;
        var value = JsonSerializer.SerializeToElement(constraint.Value);
        if (!IsValidConstraint(keyword, value, type))
            throw new JsonSchemaConversionException($"Unsupported or invalid constraint '{keyword}' at '{path}'.");
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

    private static bool IsValidConstraint(string keyword, JsonElement value, string type) => keyword switch
    {
        "enum" => IsValidEnum(value),
        // Dataset-wide semantics cannot be enforced by a schema for one record.
        "unique" or "primaryKey" => value.ValueKind == JsonValueKind.False,
        _ => IsValidTypedConstraint(keyword, value, type)
    };

    private static bool IsValidTypedConstraint(string keyword, JsonElement value, string type) => type switch
    {
        "string" => IsValidStringConstraint(keyword, value),
        "integer" or "number" => IsValidNumericConstraint(keyword, value),
        "array" => IsValidArrayConstraint(keyword, value),
        "object" => IsValidObjectConstraint(keyword, value),
        _ => false
    };

    private static bool IsValidStringConstraint(string keyword, JsonElement value) => keyword switch
    {
        "minLength" or "maxLength" => NonNegativeInteger(value),
        "pattern" => value.ValueKind == JsonValueKind.String,
        _ => false
    };

    private static bool IsValidNumericConstraint(string keyword, JsonElement value) => keyword switch
    {
        "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" => value.ValueKind == JsonValueKind.Number,
        "multipleOf" => value.ValueKind == JsonValueKind.Number && value.GetDouble() > 0,
        _ => false
    };

    private static bool IsValidArrayConstraint(string keyword, JsonElement value) => keyword switch
    {
        "minItems" or "maxItems" => NonNegativeInteger(value),
        "uniqueItems" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false
    };

    private static bool IsValidObjectConstraint(string keyword, JsonElement value) => keyword switch
    {
        "minProperties" or "maxProperties" => NonNegativeInteger(value),
        "required" => value.ValueKind == JsonValueKind.Array &&
            value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
        _ => false
    };

    private static bool IsValidEnum(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 &&
        value.EnumerateArray().Select(item => item.GetRawText()).Distinct().Count() == value.GetArrayLength();

    private static bool NonNegativeInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0;
}

public sealed class JsonSchemaConversionException(string message) : InvalidOperationException(message);
