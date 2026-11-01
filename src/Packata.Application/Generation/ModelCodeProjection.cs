using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

/// <summary>Model expressions augment the versioned canonical Didot context, independently of CLR capacity for Python.</summary>
internal sealed class ModelCodeProjection(string profile, List<GenerationDiagnostic> diagnostics)
{
    private readonly bool python = profile.StartsWith("python-", StringComparison.Ordinal);
    private readonly bool pydantic = profile == "python-pydantic";
    private readonly HashSet<string> types = new(["OptionalValue", "System", "JsonPropertyName", "UNSET", "Unset", "BaseModel", "Field", "ConfigDict", "Literal", "Annotated", "Decimal", "date", "time", "datetime", "dataclass", "field", "int", "str", "float", "bool", "bytes", "list", "dict", "object"], StringComparer.Ordinal);
    private readonly List<Dictionary<string, object?>> declarations = [];
    private static readonly HashSet<string> PythonKeywords = new(("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self UNSET Unset BaseModel Field ConfigDict Literal Annotated Decimal date time datetime dataclass field int str float bool bytes list dict object schema schema_json mro json copy validate construct parse_obj parse_raw parse_file from_orm update_forward_refs").Split(' '), StringComparer.Ordinal);

    internal static bool Supports(string? value) => value is "csharp-class" or "python-class" or "python-dataclass" or "python-pydantic";

    internal void Apply(Dictionary<string, object?> context, DataSchema schema)
    {
        var target = (Dictionary<string, object?>)context["target"]!;
        var name = Unique(((string)target["name"]!).TrimStart('@'), types, true);
        var fields = (Dictionary<string, object?>[])context["fields"]!;
        Fields(fields, schema.Fields, name);
        declarations.Add(GenerationData.Object(("name", name), ("fields", fields)));
        target["name"] = name;
        target["declarations"] = declarations;
        target["imports"] = profile switch
        {
            "python-class" => new[] { "datetime", "decimal", "enum" },
            "python-dataclass" => ["datetime", "decimal", "enum", "dataclasses"],
            "python-pydantic" => ["datetime", "decimal", "typing", "pydantic"],
            _ => ["System", "System.Text.Json.Serialization"]
        };
        ((Dictionary<string, object?>)context["asset"]!)["generated_name"] = name;
        if (schema.PrimaryKey.Count > 0 || schema.Relationships.Count > 0 || !schema.AllowAdditionalProperties || schema.EnforcePropertyOrder)
            diagnostics.Add(new("MODEL_SCHEMA_SEMANTICS", "Warning", "$schema", "Keys, relationships, additional-property and ordering policies require separate validation."));
    }

    private string Unique(string value, HashSet<string> used, bool type = false)
    {
        if (!python) return CSharpNames.Unique(value, used, type);
        // Restrict identifiers to ASCII so normalization cannot create hidden Python collisions.
        var root = Regex.Replace(ContractJson.Redact(value), "[^a-zA-Z0-9_]", "_");
        if (root.Length == 0) root = "field";
        if (type) root = char.ToUpperInvariant(root[0]) + root[1..];
        if (root[0] == '_' || char.IsDigit(root[0]) || root.StartsWith("model_", StringComparison.Ordinal)) root = "field_" + root;
        if (PythonKeywords.Contains(root)) root += "_";
        var name = root;
        var suffix = 2;
        while (!used.Add(name)) name = root + "_" + suffix++;
        return name;
    }

    private void Fields(Dictionary<string, object?>[] contexts, IReadOnlyList<DataField> fields, string owner)
    {
        var used = new HashSet<string>(python ? PythonKeywords.Concat(types) : [owner.TrimStart('@'), "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize"], StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            var blocked = pydantic ? new HashSet<string>(used.Concat(fields.Where((_, j) => j != i).Select(f => f.Name)), StringComparer.Ordinal) : used;
            var name = Unique(fields[i].Name, blocked);
            used.Add(name.TrimStart('@'));
            if (python) types.Add(name);
            contexts[i]["generated_name"] = name;
            Field(contexts[i], fields[i], owner + "_" + name.TrimStart('@'), false);
        }
        if (pydantic && fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            Error("MODEL_NAME", owner, "Duplicate original field names cannot have unambiguous Pydantic aliases.");
    }

    private string Field(Dictionary<string, object?> context, DataField field, string owner, bool item)
    {
        var path = (string)context["source_path"]!;
        var resolution = FieldTypeResolver.Resolve(field, new(SourcePath: path));
        var expression = Type(context, resolution, owner, path);
        if (field.Nullable) expression += python ? " | None" : "?";
        var target = (Dictionary<string, object?>)context["target"]!;
        target["value_type_expression"] = expression;
        if (!item && !field.Required)
        {
            if (pydantic && !field.Nullable) Error("MODEL_PRESENCE", path, "Pydantic optional non-nullable fields need a legitimate default; none is inferred. Make presence required or choose a sentinel-based class/dataclass target.");
            else if (!pydantic) expression = python ? expression + " | Unset" : $"OptionalValue<{expression}>";
        }
        target["type_expression"] = expression;
        target["required_keyword"] = field.Required ? "required " : "";
        target["initializer"] = !item && !field.Required && python ? (pydantic ? " = None" : " = UNSET") : "";
        target["source_name_literal"] = Literal(field.Name);
        target["description_literal"] = Literal(field.Description ?? "");
        var arguments = PydanticArguments(field, resolution, path, item);
        target["field_arguments"] = arguments;
        if (pydantic && item && arguments.Length > 0)
            target["type_expression"] = expression = $"Annotated[{expression}, Field({arguments})]";
        if (!pydantic && (field.Constraints.Any(c => c.Kind is not ("required" or "precision" or "scale" or "integerWidth" or "signed" or "timezone" or "defaultTimezone")) || field.QualityRules.Count > 0 || field.ValueDomain is not null))
            diagnostics.Add(new("MODEL_ANNOTATIONS", "Warning", path, "Plain model annotations do not enforce constraints, defaults, value domains or quality rules; inspect canonical context metadata."));
        return expression;
    }

    private string Type(Dictionary<string, object?> context, FieldTypeResolution r, string owner, string path)
    {
        var f = r.Field;
        if (f.Map is { } map)
        {
            if (f.Children.Count > 0 || f.Items is not null || r.LogicalType is not ("object" or "map")) Error("MODEL_SHAPE", path, "Map cannot also declare children/items and requires map/object logical type.");
            if (map.Key.Nullable || map.Key.LogicalType != "string") Error("MODEL_MAP_KEY", path, "Only non-nullable string map keys are supported.");
            var key = Field((Dictionary<string, object?>)context["map_key"]!, map.Key, owner + "_Key", true);
            var value = Field((Dictionary<string, object?>)context["map_value"]!, map.Value, owner + "_Value", true);
            return python ? $"dict[{key}, {value}]" : $"global::System.Collections.Generic.Dictionary<{key}, {value}>";
        }
        if (f.Children.Count > 0)
        {
            var name = Unique(owner, types, true);
            var children = (Dictionary<string, object?>[])context["children"]!;
            Fields(children, f.Children, name);
            declarations.Add(GenerationData.Object(("name", name), ("fields", children)));
            return name;
        }
        if (f.Items is not null)
        {
            var value = Field((Dictionary<string, object?>)context["items"]!, f.Items, owner + "_Item", true);
            return python ? $"list[{value}]" : value + "[]";
        }
        if (!python)
        {
            foreach (var d in r.Diagnostics.Where(d => d.Target == TypeMappingTarget.Runtime && d.Severity == TypeMappingSeverity.Error))
                Error(d.Code, path, d.Message);
            if (r.RuntimeType is not null) return CSharpNames.TypeExpression(r.RuntimeType);
        }
        else
        {
            if (r.Parameters.TemporalPrecision > 6) Error("MODEL_TEMPORAL_PRECISION", path, "Python datetime/time support at most six fractional second digits.");
            if (r.Parameters.TemporalSemantics is not null) diagnostics.Add(new("MODEL_TIMEZONE", "Warning", path, "Timezone/offset semantics remain metadata and are not enforced by this model."));
            var scalar = r.LogicalType switch
            {
                "string" => "str", "integer" or "year" => "int", "yearmonth" => "str", "boolean" => "bool", "number" => r.SourceFormat is "fp32" or "fp64" ? "float" : "Decimal",
                "date" => "date", "time" => "time", "datetime" or "timestamp" => "datetime",
                "object" => "dict[str, object]", _ => null
            };
            if (scalar is not null)
            {
                if (pydantic && f.ValueDomain?.AllowedValues is { Count: > 0 } values)
                {
                    if (values.All(v => v.Value is string or bool or byte or short or int or long))
                        scalar = "Literal[" + string.Join(", ", values.Select(v => PythonValue(v.Value))) + "]";
                    else Error("MODEL_ENUM", path, "Only string, integer and boolean enum literals are supported.");
                }
                else if (pydantic && f.ValueDomain?.AllowedValues is { Count: 0 })
                    Error("MODEL_ENUM", path, "An empty enum has no representable model values.");
                if (pydantic && r.LogicalType == "integer" && r.Parameters.IntegerWidth is { } width && r.Parameters.Signed is { } signed)
                {
                    var min = signed ? -(System.Numerics.BigInteger.One << (width - 1)) : System.Numerics.BigInteger.Zero;
                    var max = (System.Numerics.BigInteger.One << (signed ? width - 1 : width)) - 1;
                    scalar = $"Annotated[{scalar}, Field(ge={min}, le={max})]";
                }
                return scalar;
            }
        }
        Error("MODEL_TYPE", path, "The model target cannot represent this field without losing its declared type.");
        return "object";
    }

    private string PydanticArguments(DataField f, FieldTypeResolution r, string path, bool item)
    {
        if (!pydantic) return "";
        var args = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!item && !f.Required && f.Nullable) args.Add("default=None");
        if (!item) args.Add("alias=" + Literal(f.Name));
        if (f.Description is not null) args.Add("description=" + Literal(f.Description));
        foreach (var c in f.Constraints)
        {
            var numeric = r.LogicalType is "integer" or "number";
            var sized = r.LogicalType is "string" or "array" || f.Map is not null;
            var key = c.Kind switch
            {
                "minimum" when numeric => "ge", "maximum" when numeric => "le",
                "exclusiveMinimum" when numeric => "gt", "exclusiveMaximum" when numeric => "lt",
                "minLength" or "minItems" when sized => "min_length", "maxLength" or "maxItems" when sized => "max_length",
                "precision" when r.LogicalType == "number" => "max_digits", "scale" when r.LogicalType == "number" => "decimal_places",
                _ => null
            };
            if (key is not null)
            {
                var number = JToken.FromObject(c.Value ?? new object());
                if (!seen.Add(key)) Error("MODEL_CONSTRAINT", path, $"Multiple constraints map to '{key}'; combine them before generation.");
                else if (number.Type is JTokenType.Integer or JTokenType.Float
                    && double.TryParse(Convert.ToString(c.Value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                    && (key is not ("min_length" or "max_length" or "max_digits" or "decimal_places") || number.Type == JTokenType.Integer && value >= 0))
                    args.Add(key + "=" + (r.LogicalType == "number" && r.SourceFormat is not ("fp32" or "fp64") && number.Type == JTokenType.Float
                        ? "Decimal(" + Literal(Convert.ToString(c.Value, CultureInfo.InvariantCulture)!) + ")" : PythonValue(c.Value)));
                else Error("MODEL_CONSTRAINT", path, $"Constraint '{c.Kind}' requires a numeric value.");
            }
            else if (c.Kind == "enum" && f.ValueDomain?.AllowedValues is null)
                diagnostics.Add(new("MODEL_ENUM", "Warning", path, "This enum declaration has no canonical allowed-value domain and is not enforced."));
            else if (c.Kind is not ("required" or "enum" or "integerWidth" or "signed"))
                diagnostics.Add(new("MODEL_CONSTRAINT", "Warning", path, $"Pydantic generation does not enforce '{c.Kind}'; it remains in canonical metadata."));
        }
        if (f.QualityRules.Count > 0 || f.ValueDomain?.Reference is not null || f.Format is not null && r.LogicalType == "string")
            diagnostics.Add(new("MODEL_VALIDATION", "Warning", path, "Quality rules, referenced domains and string formats need separate validation."));
        return string.Join(", ", args);
    }

    private string Literal(string value) => python
        ? Newtonsoft.Json.JsonConvert.ToString(ContractJson.Redact(value), '"', Newtonsoft.Json.StringEscapeHandling.Default)
        : GenerationData.Literal(value);
    private string PythonValue(object? value) => value switch
    {
        null => "None", string text => Literal(text), bool b => b ? "True" : "False",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "None"
    };
    private void Error(string code, string path, string message) => diagnostics.Add(new(code, "Error", path, message));
}
