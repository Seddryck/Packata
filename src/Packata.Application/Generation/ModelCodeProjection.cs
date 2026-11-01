using System.Globalization;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

/// <summary>Model expressions augment the versioned canonical Didot context, independently of CLR capacity for Python.</summary>
internal sealed partial class ModelCodeProjection(string profile, List<GenerationDiagnostic> diagnostics)
{
    private const string DateTimeType = "datetime";
    private const string ObjectType = "object";
    private const string StringType = "string";
    private const string NumberType = "number";
    private const string WarningSeverity = "Warning";
    private readonly bool python = profile.StartsWith("python-", StringComparison.Ordinal);
    private readonly bool pydantic = profile == "python-pydantic";
    private readonly HashSet<string> types = new(["OptionalValue", "System", "JsonPropertyName", "UNSET", "Unset", "BaseModel", "Field", "ConfigDict", "Literal", "Annotated", "Decimal", "date", "time", DateTimeType, "dataclass", "field", "int", "str", "float", "bool", "bytes", "list", "dict", ObjectType], StringComparer.Ordinal);
    private readonly List<Dictionary<string, object?>> declarations = [];
    private static readonly HashSet<string> PythonKeywords = new(("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self UNSET Unset BaseModel Field ConfigDict Literal Annotated Decimal date time datetime dataclass field int str float bool bytes list dict object schema schema_json mro json copy validate construct parse_obj parse_raw parse_file from_orm update_forward_refs").Split(' '), StringComparer.Ordinal);

    internal static bool Supports(string? value) => value is "csharp-class" or "python-class" or "python-dataclass" or "python-pydantic";

    internal void Apply(Dictionary<string, object?> context, DataSchema schema)
    {
        var target = GenerationData.Required<Dictionary<string, object?>>(context, "target");
        var name = Unique(GenerationData.Required<string>(target, "name").TrimStart('@'), types, true);
        var fields = GenerationData.Required<Dictionary<string, object?>[]>(context, "fields");
        Fields(fields, schema.Fields, name);
        declarations.Add(GenerationData.Object(("name", name), ("fields", fields)));
        target["name"] = name;
        target["declarations"] = declarations;
        target["imports"] = profile switch
        {
            "python-class" => new[] { DateTimeType, "decimal", "enum" },
            "python-dataclass" => [DateTimeType, "decimal", "enum", "dataclasses"],
            "python-pydantic" => [DateTimeType, "decimal", "typing", "pydantic"],
            _ => ["System", "System.Text.Json.Serialization"]
        };
        GenerationData.Required<Dictionary<string, object?>>(context, "asset")["generated_name"] = name;
        if (schema.PrimaryKey.Count > 0 || schema.Relationships.Count > 0 || !schema.AllowAdditionalProperties || schema.EnforcePropertyOrder)
            diagnostics.Add(new("MODEL_SCHEMA_SEMANTICS", WarningSeverity, "$schema", "Keys, relationships, additional-property and ordering policies require separate validation."));
    }

    private string Unique(string value, HashSet<string> used, bool type = false)
    {
        if (!python) return CSharpNames.Unique(value, used, type);
        // Restrict identifiers to ASCII so normalization cannot create hidden Python collisions.
        var root = new string(ContractJson.Redact(value).Select(IdentifierCharacter).ToArray());
        if (root.Length == 0) root = "field";
        if (type) root = char.ToUpperInvariant(root[0]) + root[1..];
        if (root[0] == '_' || char.IsDigit(root[0]) || root.StartsWith("model_", StringComparison.Ordinal)) root = "field_" + root;
        if (PythonKeywords.Contains(root)) root += "_";
        var name = root;
        var suffix = 2;
        while (!used.Add(name)) name = root + "_" + suffix++;
        return name;
    }

    private static char IdentifierCharacter(char value) => (char.IsAsciiLetterOrDigit(value) || value == '_') ? value : '_';

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
        var path = GenerationData.Required<string>(context, "source_path");
        var resolution = FieldTypeResolver.Resolve(field, new(SourcePath: path));
        var expression = Type(context, resolution, owner, path);
        if (field.Nullable) expression += python ? " | None" : "?";
        var target = GenerationData.Required<Dictionary<string, object?>>(context, "target");
        target["value_type_expression"] = expression;
        expression = PresenceExpression(field, expression, path, item);
        target["type_expression"] = expression;
        target["required_keyword"] = field.Required ? "required " : "";
        target["initializer"] = Initializer(field, item);
        target["source_name_literal"] = Literal(field.Name);
        target["description_literal"] = Literal(field.Description ?? "");
        var arguments = PydanticArguments(field, resolution, path, item);
        target["field_arguments"] = arguments;
        if (pydantic && item && arguments.Length > 0)
            target["type_expression"] = expression = $"Annotated[{expression}, Field({arguments})]";
        WarnPlainSemantics(field, path);
        return expression;
    }

    private string PresenceExpression(DataField field, string expression, string path, bool item)
    {
        if (item || field.Required) return expression;
        if (!pydantic) return python ? expression + " | Unset" : $"OptionalValue<{expression}>";
        if (!field.Nullable)
            Error("MODEL_PRESENCE", path, "Pydantic optional non-nullable fields need a legitimate default; none is inferred. Make presence required or choose a sentinel-based class/dataclass target.");
        return expression;
    }

    private string Initializer(DataField field, bool item)
    {
        if (item || field.Required || !python) return "";
        return pydantic ? " = None" : " = UNSET";
    }

    private void WarnPlainSemantics(DataField field, string path)
    {
        if (pydantic) return;
        var constraints = field.Constraints.Any(c => c.Kind is not ("required" or "precision" or "scale" or "integerWidth" or "signed" or "timezone" or "defaultTimezone"));
        if (constraints || field.QualityRules.Count > 0 || field.ValueDomain is not null)
            diagnostics.Add(new("MODEL_ANNOTATIONS", WarningSeverity, path, "Plain model annotations do not enforce constraints, defaults, value domains or quality rules; inspect canonical context metadata."));
    }

    private string Type(Dictionary<string, object?> context, FieldTypeResolution resolution, string owner, string path)
    {
        var field = resolution.Field;
        if (field.Map is { } map) return MapType(context, resolution, map, owner, path);
        if (field.Children.Count > 0) return NestedType(context, field, owner);
        if (field.Items is { } item)
        {
            var value = Field(GenerationData.Required<Dictionary<string, object?>>(context, "items"), item, owner + "_Item", true);
            return python ? $"list[{value}]" : value + "[]";
        }
        return python ? PythonType(resolution, path) : ClrType(resolution, path);
    }

    private string MapType(Dictionary<string, object?> context, FieldTypeResolution resolution, DataMap map, string owner, string path)
    {
        var field = resolution.Field;
        if (field.Children.Count > 0 || field.Items is not null || resolution.LogicalType is not (ObjectType or "map"))
            Error("MODEL_SHAPE", path, "Map cannot also declare children/items and requires map/object logical type.");
        if (map.Key.Nullable || map.Key.LogicalType != StringType)
            Error("MODEL_MAP_KEY", path, "Only non-nullable string map keys are supported.");
        var key = Field(GenerationData.Required<Dictionary<string, object?>>(context, "map_key"), map.Key, owner + "_Key", true);
        var value = Field(GenerationData.Required<Dictionary<string, object?>>(context, "map_value"), map.Value, owner + "_Value", true);
        return python ? $"dict[{key}, {value}]" : $"global::System.Collections.Generic.Dictionary<{key}, {value}>";
    }

    private string NestedType(Dictionary<string, object?> context, DataField field, string owner)
    {
        var name = Unique(owner, types, true);
        var children = GenerationData.Required<Dictionary<string, object?>[]>(context, "children");
        Fields(children, field.Children, name);
        declarations.Add(GenerationData.Object(("name", name), ("fields", children)));
        return name;
    }

    private string ClrType(FieldTypeResolution resolution, string path)
    {
        foreach (var diagnostic in resolution.Diagnostics.Where(d => d.Target == TypeMappingTarget.Runtime && d.Severity == TypeMappingSeverity.Error))
            Error(diagnostic.Code, path, diagnostic.Message);
        if (resolution.RuntimeType is { } runtime) return CSharpNames.TypeExpression(runtime);
        return UnsupportedType(path);
    }

    private string UnsupportedType(string path)
    {
        Error("MODEL_TYPE", path, "The model target cannot represent this field without losing its declared type.");
        return ObjectType;
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
