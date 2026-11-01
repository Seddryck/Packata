using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

/// <summary>Render the validated Spark document as native Python constructors, preserving its metadata verbatim.</summary>
internal static class PySparkCodeProjection
{
    internal static void Apply(Dictionary<string, object?> context, DataAsset asset,
        List<GenerationDiagnostic> diagnostics, CancellationToken token)
    {
        var result = new StructuredSchemaExport("spark-json", diagnostics, token).Export(asset);
        if (result.Content is null) return;
        var schema = JObject.Parse(result.Content);
        var target = (Dictionary<string, object?>)context["target"]!;
        var name = new string(((string)target["name"]!).TrimStart('@').Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (name.Length == 0 || char.IsDigit(name[0])) name = "Schema_" + name;
        // Avoid collisions with module imports, constructors, and Python constants.
        if (name is "False" or "True" or "None" or "S" or "Json") name += "Schema";
        target["name"] = name;
        ((Dictionary<string, object?>)context["asset"]!)["generated_name"] = name;
        target["schema_expression"] = Type(schema);
        ApplyFields((Dictionary<string, object?>[])context["fields"]!, (JArray)schema["fields"]!);
    }

    private static void ApplyFields(Dictionary<string, object?>[] contexts, JArray fields)
    {
        for (var i = 0; i < contexts.Length; i++)
        {
            var field = fields[i];
            var target = (Dictionary<string, object?>)contexts[i]["target"]!;
            target["field_expression"] = Field(field);
            ApplyType(contexts[i], field["type"]!);
        }
    }

    private static void ApplyType(Dictionary<string, object?> context, JToken type)
    {
        ((Dictionary<string, object?>)context["target"]!)["type_expression"] = Type(type);
        if (type is not JObject shape) return;
        switch ((string?)shape["type"])
        {
            case "struct": ApplyFields((Dictionary<string, object?>[])context["children"]!, (JArray)shape["fields"]!); break;
            case "array" when context["items"] is Dictionary<string, object?> item: ApplyType(item, shape["elementType"]!); break;
            case "map" when context["map_key"] is Dictionary<string, object?> key && context["map_value"] is Dictionary<string, object?> value:
                ApplyType(key, shape["keyType"]!); ApplyType(value, shape["valueType"]!); break;
        }
    }

    private static string Literal(string value) => JsonConvert.ToString(value, '"', StringEscapeHandling.Default);
    private static string Bool(JToken value) => (bool)value ? "True" : "False";
    private static string Field(JToken field) => $"S.StructField({Literal((string)field["name"]!)}, {Type(field["type"]!)}, nullable={Bool(field["nullable"]!)}, metadata=json.loads({Literal(field["metadata"]!.ToString(Formatting.None))}))";
    private static string Type(JToken type)
    {
        if (type is JObject shape)
            return (string?)shape["type"] switch
            {
                "struct" => $"S.StructType([{string.Join(", ", ((JArray)shape["fields"]!).Select(Field))}])",
                "array" => $"S.ArrayType({Type(shape["elementType"]!)}, containsNull={Bool(shape["containsNull"]!)})",
                "map" => $"S.MapType({Type(shape["keyType"]!)}, {Type(shape["valueType"]!)}, valueContainsNull={Bool(shape["valueContainsNull"]!)})",
                _ => throw new InvalidOperationException("Unvalidated Spark shape.")
            };
        var scalar = (string)type!;
        if (scalar.StartsWith("decimal(", StringComparison.Ordinal)) return "S.DecimalType(" + scalar[8..];
        var constructor = scalar switch
        {
            "byte" => "Byte", "short" => "Short", "integer" => "Integer", "long" => "Long", "float" => "Float",
            "double" => "Double", "string" => "String", "boolean" => "Boolean", "date" => "Date", "timestamp" => "Timestamp",
            _ => throw new InvalidOperationException("Unvalidated Spark scalar.")
        };
        return $"S.{constructor}Type()";
    }
}
