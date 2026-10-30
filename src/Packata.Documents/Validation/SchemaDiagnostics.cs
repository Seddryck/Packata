using System.Text.Json;
using Json.More;
using Json.Schema;
using Packata.Core.Contracts;

namespace Packata.Documents.Validation;

/// <summary>Projects assertions without treating rejected alternatives as unconditional failures.</summary>
internal sealed class SchemaDiagnostics(JsonElement schema, DescriptorProfile profile)
{
    private static readonly HashSet<string> Applicators = ["properties", "items", "allOf", "$ref", "then", "else", "dependentSchemas"];
    private static readonly HashSet<string> Unions = ["anyOf", "oneOf"];
    private readonly List<ValidationFinding> findings = [];

    internal static IReadOnlyList<ValidationFinding> Project(JsonSchema schema, EvaluationResults result, JsonElement instance, DescriptorProfile profile)
    {
        var projection = new SchemaDiagnostics(schema.Root.Source, profile);
        projection.Visit(result, instance);
        if (!result.IsValid && projection.findings.Count == 0)
            projection.Add(result, "The descriptor does not satisfy the selected schema.");
        return projection.findings.Distinct().ToArray();
    }

    private void Visit(EvaluationResults result, JsonElement instance)
    {
        if (result.IsValid) return;
        var children = result.Details?.ToArray() ?? [];
        var start = findings.Count;
        VisitChildren(result, children, instance);
        Assertions(result, children, findings.Count > start);
    }

    private void VisitChildren(EvaluationResults result, EvaluationResults[] children, JsonElement instance)
    {
        foreach (var group in children.GroupBy(child => Keyword(result, child)))
        {
            if (group.Key == "if") continue; // A false condition selects else; it is not a failed assertion.
            if (Unions.Contains(group.Key)) Union(result, group.Key, group.ToArray(), instance);
            else foreach (var child in group) Visit(child, instance);
        }
    }

    private void Assertions(EvaluationResults result, EvaluationResults[] children, bool hasCause)
    {
        if (result.Errors is null) return;
        foreach (var keyword in result.Errors.Keys)
        {
            if (Unions.Contains(keyword) && children.Any(child => Keyword(result, child) == keyword)) continue;
            if (Applicators.Contains(keyword) && hasCause) continue;
            Add(result, Assertion(result, keyword));
        }
    }

    private void Union(EvaluationResults parent, string keyword, EvaluationResults[] branches, JsonElement instance)
    {
        // The parent may have failed an unrelated property while this union passed.
        if (parent.Errors?.ContainsKey(keyword) != true) return;
        if (keyword == "oneOf" && branches.Count(branch => branch.IsValid) > 1)
        {
            Add(parent, "More than one 'oneOf' alternative matches; exactly one is required.");
            return;
        }
        var value = Pointer(instance, parent.InstanceLocation.ToString());
        var candidates = branches.Where(branch => Applicable(Resolve(branch), value)).ToArray();
        if (candidates.Length == 1)
        {
            Visit(candidates[0], instance);
            return;
        }
        var types = branches.Select(branch => TypeDescription(Resolve(branch))).Where(type => type is not null).Distinct().ToArray();
        var expected = types.Length == 0 ? "Check the declaration's shape and discriminator properties." : $"Expected {string.Join(" or ", types)}.";
        Add(parent, $"No '{keyword}' alternative matches. {expected}");
    }

    private static string Keyword(EvaluationResults parent, EvaluationResults child)
        => child.EvaluationPath.ToString()[(parent.EvaluationPath.ToString().Length + 1)..].Split('/')[0];

    private JsonElement Resolve(EvaluationResults result)
    {
        var current = schema;
        foreach (var segment in Segments(result.EvaluationPath.ToString()))
        {
            current = segment == "$ref" ? Reference(current) : Member(current, segment);
        }
        return current;
    }

    private JsonElement Reference(JsonElement current)
    {
        var reference = Member(current, "$ref");
        return reference.ValueKind == JsonValueKind.String && reference.GetString() is { } uri && uri.StartsWith('#')
            ? Pointer(schema, Uri.UnescapeDataString(uri[1..])) : default;
    }

    private static bool Applicable(JsonElement branch, JsonElement value)
    {
        if (!MatchesType(Member(branch, "type"), value) || !MatchesChoice(branch, value)) return false;
        var properties = Member(branch, "properties");
        if (properties.ValueKind != JsonValueKind.Object || value.ValueKind != JsonValueKind.Object) return true;
        return properties.EnumerateObject().All(property =>
            !value.TryGetProperty(property.Name, out var supplied) || MatchesChoice(property.Value, supplied));
    }

    private static bool MatchesChoice(JsonElement branch, JsonElement value)
    {
        var constant = Member(branch, "const");
        if (constant.ValueKind != JsonValueKind.Undefined && !constant.IsEquivalentTo(value)) return false;
        var choices = Member(branch, "enum");
        return choices.ValueKind != JsonValueKind.Array || choices.EnumerateArray().Any(choice => choice.IsEquivalentTo(value));
    }

    private static bool MatchesType(JsonElement type, JsonElement value)
        => type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind == JsonValueKind.Number,
                "integer" => Integer(value),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => true
            },
            JsonValueKind.Array => type.EnumerateArray().Any(option => MatchesType(option, value)),
            _ => true
        };

    private static bool Integer(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number;

    private string Assertion(EvaluationResults result, string keyword)
    {
        var definition = Resolve(result);
        return keyword switch
        {
            "type" => $"Expected {TypeDescription(definition) ?? "the declared JSON type"}.",
            "required" => "Required properties are missing; check the declaration against the selected profile.",
            "enum" or "const" => "The declaration must use a value permitted by the selected profile.",
            "additionalProperties" => "The declaration contains properties not permitted by the selected profile.",
            "format" => "The declaration does not use the required format.",
            _ => $"Schema assertion '{keyword}' failed."
        };
    }

    private static string? TypeDescription(JsonElement definition)
    {
        var type = Member(definition, "type");
        return type.ValueKind == JsonValueKind.String ? type.GetString() : null;
    }

    private void Add(EvaluationResults result, string message)
        => findings.Add(new(ValidationLayers.Schema, "VAL_SCHEMA", MappingSeverity.Error,
            "$" + result.InstanceLocation, $"{profile.Label}: {message}"));

    private static string[] Segments(string pointer) => pointer.Split('/').Skip(1)
        .Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();

    private static JsonElement Pointer(JsonElement root, string pointer)
        => Segments(pointer).Aggregate(root, Member);

    private static JsonElement Member(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)) return property;
        if (element.ValueKind == JsonValueKind.Array && int.TryParse(name, out var index) && index >= 0 && index < element.GetArrayLength()) return element[index];
        return default;
    }
}
