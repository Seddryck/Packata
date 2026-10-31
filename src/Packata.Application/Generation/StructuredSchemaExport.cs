using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

/// <summary>Target-neutral shape/parameter projection followed by structured document serialization.</summary>
internal sealed class StructuredSchemaExport(string format, List<GenerationDiagnostic> diagnostics, CancellationToken token, SchemaDocumentOptions? options = null)
{
    private sealed record Field(string Name, string Path, string Kind, bool Nullable, bool Required,
        TypeParameters Parameters, IReadOnlyList<Field> Children, Field? Items, string? Description, Field? MapKey, Field? MapValue);
    private readonly HashSet<string> recordNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Parameters = new(["precision", "scale", "integerWidth", "signed", "timezone", "timezoneName"], StringComparer.Ordinal);

    internal GenerationResult Export(DataAsset asset)
    {
        if (options?.Namespace is { } ns && ns.Split('.').Any(part => !Regex.IsMatch(part, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            Error(asset.Id, "Avro namespace must be dot-separated valid Avro identifiers.");
        if (asset.Schema!.PrimaryKey.Count > 0 || asset.Schema.Relationships.Count > 0)
            Warn("SCHEMA_RELATIONAL", asset.Id, "Primary keys and relationships are not enforced by Avro or Spark schema documents.");
        var fields = asset.Schema!.Fields.Select(f => Project(f, asset.Id + "/" + f.Name)).ToArray();
        JToken root = format == "spark-json" ? SparkStruct(fields) : AvroRecord(options?.Name ?? asset.Name, fields);
        token.ThrowIfCancellationRequested();
        var content = diagnostics.Any(d => d.Severity == "Error") ? null : root.ToString(Formatting.Indented).ReplaceLineEndings("\n");
        return new(content, format == "avro" ? "schema.avsc" : "schema.spark.schema.json", "json", asset.Id, null, null, diagnostics)
            { MediaType = format == "avro" ? "application/avro+json" : "application/json" };
    }

    private Field Project(DataField field, string path)
    {
        token.ThrowIfCancellationRequested();
        var type = FieldTypeResolver.Resolve(field.LogicalType == "map" && field.Map is not null ? field with { LogicalType = "object", Map = null } : field, new(SourcePath: path));
        diagnostics.AddRange(type.Diagnostics.Where(d => d.Target == TypeMappingTarget.Source && d.FieldPath == path)
            .Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), path, ContractJson.Redact(d.Message), format)));
        if (!field.Required) Warn("SCHEMA_REQUIRED", path, "This target describes field values and cannot enforce property presence separately from value nullability.");
        foreach (var constraint in field.Constraints.Where(c => !Parameters.Contains(c.Kind)))
            Warn("SCHEMA_CONSTRAINT", path, $"Constraint '{constraint.Kind}' is not enforced by this schema format; no default or validation rule is inferred.");
        if (field.PhysicalType is not null) Warn("SCHEMA_PHYSICAL", path, "Database physical types are not portable schema types; canonical logical parameters are used.");
        var kind = field.LogicalType == "map" ? "map" : type.LogicalType ?? "unsupported";
        if (kind == "string" && field.Format is not null)
            Warn("SCHEMA_FORMAT", path, "String format annotations are not enforced by the target string representation.");
        if (kind == "integer") kind = Integer(type);
        if (kind == "number") kind = field.Format?.ToLowerInvariant() switch { "fp32" => "float", "fp64" => "double", _ => "decimal" };
        if (kind == "decimal" && (type.Parameters.Precision is null || type.Parameters.Scale is null))
            Error(path, "Decimal schemas require explicit precision and scale; no precision is inferred.");
        if (kind == "decimal" && format == "spark-json" && type.Parameters.Precision > 38)
            Error(path, "Spark DecimalType supports at most 38 digits of precision.");
        if (field.QualityRules.Count > 0) Warn("SCHEMA_QUALITY", path, "Quality rules are not enforced by schema serialization.");
        if (kind is "timestamp" or "datetime" or "time")
        {
            if (type.Parameters.TemporalPrecision is not null && type.Parameters.TemporalPrecision < 6) Warn("SCHEMA_PRECISION", path, "Microsecond representation retains values but does not enforce a coarser declared temporal precision.");
            if (type.Parameters.TemporalPrecision > 6) Error(path, "This target retains at most six fractional second digits.");
            if (type.Parameters.Timezone is not null || type.Parameters.TemporalSemantics == TemporalSemantics.Offset)
                Warn("SCHEMA_TIMEZONE", path, "The target retains the instant, not the original offset or named timezone.");
            if (format == "spark-json" && (kind == "time" || type.Parameters.TemporalSemantics == TemporalSemantics.Local))
                Error(path, "Spark 3.x portable JSON export does not support time-only or local timestamp values. Declare instant semantics for timestamps.");
        }
        if (kind == "object" && field.Children.Count == 0) Error(path, "An opaque object has no declared record fields; supply a nested schema.");
        if (kind == "map" && field.Map is null) Error(path, "The canonical map has no explicit portable key/value contract. Map keys must be non-null strings; supply an explicit supported shape before export.");
        return new(field.Name, path, kind, field.Nullable, field.Required, type.Parameters,
            field.Children.Select(f => Project(f, path + "/" + f.Name)).ToArray(),
            field.Items is null ? null : Project(field.Items, path + "/items"), field.Description,
            field.Map is null ? null : Project(field.Map.Key, path + "/map/key"),
            field.Map is null ? null : Project(field.Map.Value, path + "/map/value"));
    }

    private string Integer(FieldTypeResolution type)
    {
        var width = type.Parameters.IntegerWidth ?? 32;
        var signed = type.Parameters.Signed ?? true;
        if (!signed && width == 64) { Error(type.FieldPath, "Unsigned 64-bit integers cannot be represented losslessly by Avro/Spark signed integers."); return "long"; }
        var bits = signed ? width : width + 1;
        var kind = format == "spark-json" ? bits <= 8 ? "byte" : bits <= 16 ? "short" : bits <= 32 ? "integer" : "long" : bits <= 32 ? "int" : "long";
        if (!signed || format == "avro" && width < 32)
            Warn("SCHEMA_RANGE", type.FieldPath, "The target integer domain is wider than the source; source range constraints are not enforced.");
        return kind;
    }

    private JObject SparkStruct(IReadOnlyList<Field> fields)
    {
        CheckFields(fields, avro: false);
        return new() { ["type"] = "struct", ["fields"] = new JArray(fields.Select(f => new JObject {
            ["name"] = f.Name, ["type"] = SparkType(f), ["nullable"] = f.Nullable, ["metadata"] = new JObject() })) };
    }
    private JToken SparkType(Field field)
    {
        token.ThrowIfCancellationRequested();
        return field.Kind switch {
            "object" => SparkStruct(field.Children),
            "map" when MapSupported(field) => new JObject { ["type"] = "map", ["keyType"] = "string", ["valueType"] = SparkType(field.MapValue!), ["valueContainsNull"] = field.MapValue!.Nullable },
            "array" when field.Items is not null => new JObject { ["type"] = "array", ["elementType"] = SparkType(field.Items), ["containsNull"] = field.Items.Nullable },
            "decimal" => new JValue($"decimal({field.Parameters.Precision},{field.Parameters.Scale})"),
            "datetime" or "timestamp" => new JValue("timestamp"),
            "string" or "boolean" or "byte" or "short" or "integer" or "long" or "float" or "double" or "date" => new JValue(field.Kind),
            _ => Unsupported(field)
        };
    }

    private JObject AvroRecord(string proposedName, IReadOnlyList<Field> fields)
    {
        var name = Name(proposedName);
        if (!recordNames.Add(name))
        {
            var suffix = 2; var original = name;
            while (!recordNames.Add(name = original + "_" + suffix++)) { }
        }
        CheckFields(fields, avro: true);
        return new() { ["type"] = "record", ["name"] = name, ["namespace"] = options?.Namespace ?? "packata.generated",
            ["fields"] = new JArray(fields.Select(f => {
                var field = new JObject { ["name"] = Name(f.Name), ["type"] = AvroType(f, name + "_" + Name(f.Name)) };
                if (f.Description is not null) field["doc"] = f.Description;
                return field;
            })) };
    }
    private JToken AvroType(Field field, string name)
    {
        token.ThrowIfCancellationRequested();
        JToken type = field.Kind switch {
            "object" => AvroRecord(name, field.Children),
            "map" when MapSupported(field) => new JObject { ["type"] = "map", ["values"] = AvroType(field.MapValue!, name + "_value") },
            "array" when field.Items is not null => new JObject { ["type"] = "array", ["items"] = AvroType(field.Items, name + "_item") },
            "decimal" => new JObject { ["type"] = "bytes", ["logicalType"] = "decimal", ["precision"] = field.Parameters.Precision, ["scale"] = field.Parameters.Scale },
            "date" => Logical("int", "date"),
            "time" => Logical("long", "time-micros"),
            "datetime" or "timestamp" => Logical("long", (field.Parameters.TemporalSemantics == TemporalSemantics.Local ? "local-timestamp" : "timestamp") + "-micros"),
            "string" or "boolean" or "int" or "long" or "float" or "double" => new JValue(field.Kind),
            _ => Unsupported(field)
        };
        return field.Nullable ? new JArray("null", type) : type;
    }
    private bool MapSupported(Field field)
    {
        if (field.MapKey is { Kind: "string", Nullable: false } && field.MapValue is not null) return true;
        Error(field.Path, "Portable map exports require an explicit non-null string key and declared value schema.");
        return false;
    }
    private static JObject Logical(string physical, string logical) => new() { ["type"] = physical, ["logicalType"] = logical };
    private JToken Unsupported(Field field) { Error(field.Path, $"Logical type '{field.Kind}' has no supported {format} representation."); return new JValue("string"); }
    private static string Name(string name)
    {
        var value = Regex.Replace(name, "[^A-Za-z0-9_]", "_", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return value.Length == 0 || char.IsDigit(value[0]) ? "_" + value : value;
    }
    private void CheckFields(IReadOnlyList<Field> fields, bool avro)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var name = avro ? Name(field.Name) : field.Name;
            if (avro && name != field.Name) Warn("SCHEMA_NAME", field.Path, $"Avro field name normalized to '{name}'.");
            if (!names.Add(name)) Error(field.Path, "Field names collide in the target schema. Rename the conflicting fields explicitly.");
        }
    }
    private void Error(string path, string message) => diagnostics.Add(new("SCHEMA_MAPPING", "Error", path, message, format));
    private void Warn(string code, string path, string message) => diagnostics.Add(new(code, "Warning", path, message, format));
}
