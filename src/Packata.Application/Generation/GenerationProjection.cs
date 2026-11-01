using Newtonsoft.Json;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal sealed class GenerationProjection(GenerationOptions options, string? profile,
    List<GenerationDiagnostic> diagnostics, CancellationToken token)
{
    private readonly HashSet<string> _typeNames = new(["System", "Polars", "DataTable", "PolarsSchema", "DataType",
        "TimeUnit", "Dictionary", "JsonPropertyName", "Create", "Schema", "Field", "StructType", "ListType", "MapType",
        "TimestampType", "Time32Type", "Time64Type", "Decimal128Type", "Decimal256Type", "Int8Type", "UInt8Type",
        "Int16Type", "UInt16Type", "Int32Type", "UInt32Type", "Int64Type", "UInt64Type", "FloatType", "DoubleType",
        "StringType", "BinaryType", "BooleanType", "Date32Type"], StringComparer.Ordinal);
    private readonly List<Dictionary<string, object?>> _declarations = [];

    internal Dictionary<string, object?> Build(DataContract contract, DataAsset asset)
    {
        var name = CSharpNames.Unique(options.TypeName ?? asset.Name, _typeNames, true);
        var fields = Fields(asset.Schema!.Fields.Select(f => FieldTypeResolver.Resolve(f,
            new(options.Fallback, asset.Id + "/" + f.Name))).ToArray(), name);
        _declarations.Add(GenerationData.Object(("name", name), ("fields", fields)));
        return GenerationData.Object(("context_version", 1),
            ("contract", GenerationData.Object(("id", contract.Identity.Id), ("name", contract.Identity.Name), ("version", contract.Identity.Version))),
            ("asset", GenerationData.Object(("id", asset.Id), ("name", asset.Name), ("generated_name", name), ("description", asset.Description))),
            ("fields", fields), ("target", GenerationData.Object(("profile", profile), ("name", name),
                ("namespace", CSharpNames.Namespace(options.Namespace)), ("declarations", _declarations),
                ("imports", Imports()))), ("variables", GenerationData.Variables(options.Defines)), ("diagnostics", diagnostics));
    }

    private string[] Imports() => profile switch
    {
        "csharp-record" => ["System", "System.Text.Json.Serialization"],
        "csharp-datatable" => ["System", "System.Data"],
        "csharp-polars" => ["System.Collections.Generic", "Polars.CSharp"],
        _ => []
    };

    private Dictionary<string, object?>[] Fields(IReadOnlyList<FieldTypeResolution> fields, string owner)
    {
        var used = new HashSet<string>([owner.TrimStart('@'), "Clone", "EqualityContract", "Equals", "GetHashCode", "ToString", "PrintMembers"], StringComparer.Ordinal);
        return fields.Select(field => Field(field, CSharpNames.Unique(field.Field.Name, used), owner)).ToArray();
    }

    private Dictionary<string, object?> Field(FieldTypeResolution resolution, string name, string owner)
    {
        token.ThrowIfCancellationRequested();
        AddMappingDiagnostics(resolution);
        var field = resolution.Field;
        var nestedName = resolution.Children.Count > 0 ? CSharpNames.Unique(owner.TrimStart('@') + "_" + name.TrimStart('@'), _typeNames, true) : null;
        var children = Fields(resolution.Children, nestedName ?? owner);
        var items = resolution.Items is null ? null : Field(resolution.Items, "Item", owner + "_" + name.TrimStart('@'));
        if (nestedName is not null)
            _declarations.Add(GenerationData.Object(("name", nestedName), ("fields", children)));
        var constraints = field.Constraints.Select(c => GenerationData.Object(("kind", c.Kind), ("value", GenerationData.Plain(c.Value)))).ToArray();
        var context = GenerationData.Object(("name", field.Name), ("generated_name", name), ("source_path", resolution.FieldPath),
            ("description", field.Description), ("logical_type", resolution.LogicalType), ("physical_type", field.PhysicalType),
            ("format", field.Format), ("required", field.Required), ("nullable", field.Nullable),
            ("parameters", Parameters(resolution.Parameters)), ("constraints", constraints),
            ("value_domain", Domain(field.ValueDomain)), ("children", children), ("items", items),
            ("map_key", field.Map is null ? null : Field(FieldTypeResolver.Resolve(field.Map.Key, new(options.Fallback, resolution.FieldPath + "/key")), "Key", owner + "_Key")),
            ("map_value", field.Map is null ? null : Field(FieldTypeResolver.Resolve(field.Map.Value, new(options.Fallback, resolution.FieldPath + "/value")), "Value", owner + "_Value")),
            ("extensions", GenerationData.Plain(field.Extensions)), ("mapping_diagnostics", GenerationData.Plain(resolution.Diagnostics)));
        context["target"] = Project(resolution, nestedName, children, items, constraints);
        return context;
    }

    private Dictionary<string, object?> Project(FieldTypeResolution resolution, string? nestedName,
        Dictionary<string, object?>[] children, Dictionary<string, object?>? items, object constraints)
    {
        if (profile is null) return [];
        var expression = profile == "csharp-polars" ? PolarsProjection.Expression(resolution, children, items, diagnostics)
            : ClrExpression(resolution, nestedName, items);
        if (expression is null && profile is not ("csharp-arrow" or "python-arrow") && !ModelCodeProjection.Supports(profile))
            diagnostics.Add(new("GEN_MAPPING", "Error", resolution.FieldPath, "The selected target cannot represent this field. Inspect its mapping diagnostics."));
        return GenerationData.Object(("type_expression", expression),
            ("runtime_type_expression", resolution.RuntimeType is null ? null : CSharpNames.TypeExpression(resolution.RuntimeType)),
            ("required_keyword", resolution.Required ? "required " : ""),
            ("initializer", Initializer(resolution)), ("source_name_literal", GenerationData.Literal(resolution.Field.Name)),
            ("allow_dbnull", resolution.Nullable ? "true" : "false"),
            ("metadata", Metadata(resolution, constraints)));
    }

    private string? ClrExpression(FieldTypeResolution resolution, string? nestedName, Dictionary<string, object?>? items)
    {
        if (resolution.RuntimeType is null) return null;
        if (profile == "csharp-datatable") return CSharpNames.TypeExpression(resolution.RuntimeType);
        var expression = nestedName ?? CSharpNames.TypeExpression(resolution.RuntimeType);
        if (resolution.LogicalType == "array" && items is not null)
        {
            var target = (Dictionary<string, object?>)items["target"]!;
            expression = target["type_expression"] + "[]";
        }
        return expression + (resolution.Nullable ? "?" : "");
    }

    private static string Initializer(FieldTypeResolution resolution)
        => !resolution.Required && !resolution.Nullable && resolution.RuntimeType?.IsValueType == false ? " = default!;" : "";

    private static Dictionary<string, object?>[] Metadata(FieldTypeResolution resolution, object constraints)
    {
        var values = new List<(string, string)> { ("packata.required", resolution.Required ? "true" : "false"),
            ("packata.schema", GenerationData.Literal(JsonConvert.SerializeObject(GenerationData.Plain(resolution.Field)))),
            ("packata.constraints", GenerationData.Literal(JsonConvert.SerializeObject(constraints))),
            ("packata.valueDomain", GenerationData.Literal(JsonConvert.SerializeObject(Domain(resolution.Field.ValueDomain)))) };
        if (resolution.Parameters.Precision is { } precision) values.Add(("packata.precision", precision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (resolution.Parameters.Scale is { } scale) values.Add(("packata.scale", scale.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return values.Select(v => GenerationData.Object(("key", GenerationData.Literal(v.Item1)), ("value", v.Item2))).ToArray();
    }

    private void AddMappingDiagnostics(FieldTypeResolution resolution)
    {
        foreach (var diagnostic in resolution.Diagnostics.Where(d => d.FieldPath == resolution.FieldPath))
        {
            // The model projection handles canonical maps independently of CLR scalar resolution.
            if (ModelCodeProjection.Supports(profile) && resolution.Field.Map is not null && diagnostic.Code == "UnsupportedType") continue;
            var relevant = diagnostic.Target == TypeMappingTarget.Source
                || (diagnostic.Target == TypeMappingTarget.Runtime && profile is "csharp-record" or "csharp-datatable");
            var severity = relevant ? diagnostic.Severity.ToString() : "Information";
            if (options.Fallback != TypeMappingFallback.Reject && severity == "Error") severity = "Warning";
            diagnostics.Add(new(diagnostic.Code, severity, diagnostic.FieldPath, diagnostic.Message, diagnostic.Target.ToString()));
        }
    }

    private static object Parameters(TypeParameters p) => GenerationData.Object(("integer_width", p.IntegerWidth), ("signed", p.Signed),
        ("precision", p.Precision), ("scale", p.Scale), ("temporal_precision", p.TemporalPrecision),
        ("temporal_semantics", p.TemporalSemantics?.ToString()), ("timezone", p.Timezone));

    private static object? Domain(ValueDomain? domain) => domain is null ? null : GenerationData.Object(
        ("entries", domain.Entries?.Select(e => GenerationData.Object(("value", e.Value.Value), ("label", e.Label), ("description", e.Description))).ToArray()),
        ("allowed_values", domain.AllowedValues?.Select(v => v.Value).ToArray()), ("reference", GenerationData.Plain(domain.Reference)));
}
