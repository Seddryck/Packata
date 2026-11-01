using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

/// <summary>Field-aware expressions for empty DataFrames and metadata-only ML.NET schemas.</summary>
internal sealed class FrameworkCodeProjection(string profile, List<GenerationDiagnostic> diagnostics)
{
    private readonly bool dataframe = profile == "csharp-dataframe";

    internal void Apply(Dictionary<string, object?> context, DataAsset asset)
    {
        var fields = (Dictionary<string, object?>[])context["fields"]!;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++)
        {
            var field = asset.Schema!.Fields[i];
            var path = asset.Id + "/" + field.Name;
            if (!names.Add(field.Name)) Error(path, "Duplicate column names cannot be represented. Rename the conflicting field.");
            var target = (Dictionary<string, object?>)fields[i]["target"]!;
            target["schema_literal"] = GenerationData.Literal(JsonConvert.SerializeObject(GenerationData.Plain(field)));
            target["type_expression"] = dataframe ? DataFrame(field, path) : MlType(field, path);
            target["column_expression"] = target["type_expression"];
            if (field.Constraints.Count > 0 || field.QualityRules.Count > 0 || field.ValueDomain is not null || field.Format is not null)
                Warn(path, "Contract constraints, formats, quality rules and categories require separate validation.");
            if (!field.Required || field.Nullable || dataframe)
                Warn(path, dataframe
                    ? "DataFrame columns permit nulls; requiredness and value nullability must be validated separately. No rows are generated."
                    : "ML.NET cannot enforce presence or represent nullable values separately from its type-specific missing values (NaN, empty text, or key zero).");
        }
        if (asset.Schema!.PrimaryKey.Count > 0 || asset.Schema.Relationships.Count > 0 || !asset.Schema.AllowAdditionalProperties || asset.Schema.EnforcePropertyOrder)
            Warn(asset.Id, "Keys, relationships and schema property policies require separate validation.");
    }

    private string DataFrame(DataField field, string path)
    {
        var resolution = FieldTypeResolver.Resolve(field, new(SourcePath: path));
        var type = resolution.RuntimeType;
        var name = GenerationData.Literal(field.Name);
        if (resolution.LogicalType == "string") return $"new StringDataFrameColumn({name}, 0)";
        if (resolution.LogicalType is "integer" or "number" or "boolean" && type is not null)
        {
            if (type == typeof(decimal))
            {
                var parameters = resolution.Parameters;
                if (parameters.Precision is null || parameters.Scale is null || parameters.Precision > 28 || parameters.Scale > 28)
                    Error(path, "DataFrame decimal columns require explicit precision/scale of at most 28 for lossless System.Decimal storage.");
            }
            return $"new PrimitiveDataFrameColumn<{CSharpNames.TypeExpression(type)}>({name}, 0)";
        }
        Error(path, "DataFrame supports string, boolean, integer and decimal/floating columns only. Nested and temporal types require an explicit conversion before generation.");
        return "";
    }

    private string MlType(DataField field, string path)
    {
        var options = MlOptions(field, path);
        if (field.LogicalType == "array" && field.Items is { } item)
        {
            var itemType = MlScalar(item, path + "/items", MlOptions(item, path + "/items"));
            if (item.Nullable) Warn(path + "/items", "ML.NET vectors use item-specific missing values and cannot retain an independent null bitmap.");
            var dimensions = options["dimensions"] ?? new JArray(0);
            if (dimensions is not JArray { Count: > 0 } shape || shape.Any(v => !Integer(v, out var n) || n > int.MaxValue))
            { Error(path, "mlnet.dimensions must be a nonempty array of nonnegative Int32 sizes; zero means variable length."); return ""; }
            long size = 1;
            foreach (var dimension in shape) { Integer(dimension, out var n); size *= (int)n; if (size > int.MaxValue) { Error(path, "Vector size exceeds Int32 capacity."); break; } }
            if (options["keyCount"] is not null) Error(path, "Place keyCount on the vector item schema, not on the array.");
            return $"new VectorDataViewType({itemType}, new int[] {{ {string.Join(", ", shape.Select(v => ((int)v).ToString(CultureInfo.InvariantCulture)))} }})";
        }
        return MlScalar(field, path, options);
    }

    private string MlScalar(DataField field, string path, JObject options)
    {
        if (options["dimensions"] is not null) Error(path, "mlnet.dimensions applies only to an array with scalar items.");
        if (path.EndsWith("/items", StringComparison.Ordinal) && (field.Constraints.Count > 0 || field.ValueDomain is not null || field.QualityRules.Count > 0))
            Warn(path, "Vector item constraints, categories and quality rules require separate validation; declarations remain in packata.schema metadata.");
        var resolution = FieldTypeResolver.Resolve(field, new(SourcePath: path));
        var type = resolution.RuntimeType;
        if (options["keyCount"] is { } key)
        {
            var maximum = type == typeof(byte) ? byte.MaxValue : type == typeof(ushort) ? ushort.MaxValue : type == typeof(uint) ? uint.MaxValue : type == typeof(ulong) ? ulong.MaxValue : 0UL;
            if (!Integer(key, out var count) || count == 0 || count > maximum)
            { Error(path, "mlnet.keyCount requires an unsigned integer field and a positive cardinality within its storage range. Keys are one-based; zero is missing."); return ""; }
            Warn(path, "ML.NET keys are one-based categorical indices; zero is reserved for missing values. No category encoding or data conversion is generated.");
            return $"new KeyDataViewType(typeof({CSharpNames.TypeExpression(type!)}), {count.ToString(CultureInfo.InvariantCulture)}UL)";
        }
        if (resolution.LogicalType == "string") return "TextDataViewType.Instance";
        if (resolution.LogicalType == "boolean") return "BooleanDataViewType.Instance";
        if (resolution.LogicalType is "integer" or "number" && type is not null && type != typeof(decimal))
            return "NumberDataViewType." + type.Name;
        Error(path, "ML.NET supports boolean, text, integer, fp32/fp64 and vectors of these scalars. Decimal, temporal, nested object/map and nested vector types require explicit conversion.");
        return "";
    }

    private JObject MlOptions(DataField field, string path)
    {
        object? value = null;
        if (field.Extensions.TryGetNamespace("mlnet", out var direct)) value = direct;
        else if (field.Extensions.TryGetNamespace("odcs", out var odcs) && odcs.TryGetValue("customProperties", out var properties) && properties is not null)
        {
            var entries = JArray.FromObject(properties).Where(p => (string?)p["property"] == "mlnet").ToArray();
            if (entries.Length > 1) Error(path, "Declare only one mlnet custom property per field.");
            value = entries.FirstOrDefault()?["value"];
        }
        if (value is null) return new();
        var token = JToken.FromObject(value);
        if (token is not JObject result) { Error(path, "The mlnet mapping declaration must be an object."); return new(); }
        foreach (var property in result.Properties().Where(p => p.Name is not ("dimensions" or "keyCount")))
            Error(path, "Unknown mlnet mapping option: " + property.Name);
        return result;
    }

    // YAML object-valued custom properties retain scalar numbers as strings.
    private static bool Integer(JToken value, out ulong number)
        => ulong.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && value.Type is JTokenType.Integer or JTokenType.String;

    private void Error(string path, string message) => diagnostics.Add(new("GEN_FRAMEWORK_TYPE", "Error", path, message, profile));
    private void Warn(string path, string message) => diagnostics.Add(new("GEN_FRAMEWORK_SEMANTICS", "Warning", path, message, profile));
}
