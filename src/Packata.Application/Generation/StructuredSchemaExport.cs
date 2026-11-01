using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

/// <summary>Target-neutral shape/parameter projection followed by structured document serialization.</summary>
internal sealed partial class StructuredSchemaExport(string outputFormat, List<GenerationDiagnostic> diagnostics, CancellationToken token, SchemaDocumentOptions? options = null, string? profile = null)
{
    private readonly string format = outputFormat == "avro-idl" ? "avro" : outputFormat;
    private sealed record Field(string Name, string Path, string Kind, bool Nullable, bool Required,
        TypeParameters Parameters, IReadOnlyList<Field> Children, Field? Items, string? Description, Field? MapKey, Field? MapValue, IReadOnlyList<string>? EnumSymbols, JObject Metadata, DataField Source);
    private readonly HashSet<string> recordNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Parameters = new(["precision", "scale", "integerWidth", "signed", "timezone", "defaultTimezone"], StringComparer.Ordinal);

    internal GenerationResult Export(DataAsset asset)
    {
        if (options?.Namespace is { } ns && ns.Split('.').Any(part => !Regex.IsMatch(part, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            Error(asset.Id, "Avro namespace must be dot-separated valid Avro identifiers.");
        ReportSchemaPolicies(asset);
        var fields = asset.Schema!.Fields.Select(f => Project(f, asset.Id + "/" + f.Name, asset.Schema.PrimaryKey.Contains(f.Name))).ToArray();
        var metadata = SchemaConstraintMetadata.Schema(asset, format == "avro" ? Name(options?.Name ?? asset.Name) : null);
        ValidateCompatibility(asset, fields);
        JToken root = format switch {
            "iceberg-json" => IcebergSchema(asset, fields),
            "paimon-json" => PaimonSchema(asset, fields),
            "spark-json" => SparkStruct(fields, metadata),
            _ => AvroRecord(options?.Name ?? asset.Name, fields, metadata)
        };
        token.ThrowIfCancellationRequested();
        if (outputFormat == "avro-idl") return ExportIdl(root, asset.Id);
        var content = diagnostics.Any(d => d.Severity == "Error") ? null : root.ToString(Formatting.Indented).ReplaceLineEndings("\n");
        return new(content, format == "avro" ? "schema.avsc" : $"schema.{format.Replace("-json", "", StringComparison.Ordinal)}.schema.json", "json", asset.Id, null, null, diagnostics)
            { MediaType = format == "avro" ? "application/avro+json" : "application/json" };
    }

    private GenerationResult ExportIdl(JToken root, string assetId)
    {
        string? idl = null;
        if (!diagnostics.Any(d => d.Severity == "Error"))
        {
            try { idl = AvroIdlRenderer.Render(root, token); }
            catch (NotSupportedException error) { diagnostics.Add(new("AVRO_IDL_PROPERTY", "Error", assetId, error.Message, outputFormat)); }
        }
        return new(idl, "schema.avdl", "plaintext", assetId, null, null,
            diagnostics.Select(d => d.Target == "avro" ? d with { Target = outputFormat } : d).ToArray());
    }

    private void ReportSchemaPolicies(DataAsset asset)
    {
        if (format is "iceberg-json" or "paimon-json") { ReportLakehousePolicies(asset); return; }
        if (asset.Schema!.PrimaryKey.Count > 0 || asset.Schema.Relationships.Count > 0)
            Info("SCHEMA_RELATIONAL", asset.Id, "Primary keys and relationships cannot be enforced by this schema format. Their declarations are preserved in Packata metadata.");
        if (!asset.Schema.AllowAdditionalProperties || asset.Schema.EnforcePropertyOrder)
            Info("SCHEMA_PROPERTY_POLICY", asset.Id, "Additional-property and property-order policies are preserved in Packata metadata; the target schema does not independently enforce them.");
    }

    private Field Project(DataField field, string path, bool primaryKey = false, bool named = true)
    {
        token.ThrowIfCancellationRequested();
        if (format == "paimon-json" && !named && TargetMetadata.Value(field.Extensions, "paimon", "field-id") is not null)
            Error(path, "Paimon collection elements have no field IDs; supply IDs only on named row fields.");
        var type = ResolveType(field, path);
        ReportMetadataLoss(field, path);
        var kind = ProjectKind(field, type, path);
        ValidateDecimal(kind, type.Parameters, path);
        ValidateTemporal(kind, type.Parameters, path);
        ValidateShape(kind, field, path);
        var symbols = format == "avro" ? AvroEnumProjection.Symbols(field) : null;
        var projected = new Field(field.Name, path, kind, field.Nullable, field.Required, type.Parameters,
            field.Children.Select(f => Project(f, path + "/" + f.Name)).ToArray(),
            field.Items is null ? null : Project(field.Items, path + "/items", named: false), field.Description,
            field.Map is null ? null : Project(field.Map.Key, path + "/map/key", named: false),
            field.Map is null ? null : Project(field.Map.Value, path + "/map/value", named: false),
            symbols, SchemaConstraintMetadata.Field(field, NativeFieldName(field.Name, named),
                new(format, kind, type.Parameters, symbols is not null), primaryKey), field);
        AttachElementMetadata(projected);
        return projected;
    }

    private string? NativeFieldName(string source, bool named)
        => named ? (format == "avro" ? Name(source) : source) : null;

    private static void AttachElementMetadata(Field field)
    {
        if (field.Items is { Metadata.HasValues: true } item) field.Metadata["items"] = item.Metadata.DeepClone();
        var map = new JObject();
        if (field.MapKey is { Metadata.HasValues: true } key) map["key"] = key.Metadata.DeepClone();
        if (field.MapValue is { Metadata.HasValues: true } value) map["value"] = value.Metadata.DeepClone();
        if (map.HasValues) field.Metadata["map"] = map;
    }

    private FieldTypeResolution ResolveType(DataField field, string path)
    {
        var type = FieldTypeResolver.Resolve(field.LogicalType == "map" && field.Map is not null ? field with { LogicalType = "object", Map = null } : field, new(SourcePath: path));
        diagnostics.AddRange(type.Diagnostics.Where(d => d.Target == TypeMappingTarget.Source && d.FieldPath == path)
            .Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), path, ContractJson.Redact(d.Message), format)));
        return type;
    }

    private void ReportMetadataLoss(DataField field, string path)
    {
        if (format is "iceberg-json" or "paimon-json") { ReportLakehouseLoss(field, path); return; }
        if (!field.Required) Info("SCHEMA_REQUIRED", path, "The target cannot enforce property presence separately from nullability. Requiredness is preserved in Packata metadata.");
        foreach (var constraint in field.Constraints.Where(c => !Parameters.Contains(c.Kind)))
        {
            var diagnostic = SchemaConstraintDiagnostics.Constraint(field, constraint, format, path);
            if (diagnostic is not null) diagnostics.Add(diagnostic);
        }
        if (field.PhysicalType is not null) Info("SCHEMA_PHYSICAL", path, "Database physical types are not portable schema types; canonical logical parameters are used.");
        if (field.QualityRules.Count > 0) Info("SCHEMA_QUALITY", path, "Quality rules are not enforced by schema serialization.");
    }

    private string ProjectKind(DataField field, FieldTypeResolution type, string path)
    {
        var kind = field.LogicalType == "map" ? "map" : type.LogicalType ?? "unsupported";
        if (kind == "string" && field.Format is not null)
            Info("SCHEMA_FORMAT", path, "String format annotations are not enforced by the target string representation.");
        if (kind == "integer") kind = Integer(type);
        if (kind == "number") kind = field.Format?.ToLowerInvariant() switch { "fp32" => "float", "fp64" => "double", _ => "decimal" };
        return kind;
    }

    private void ValidateDecimal(string kind, TypeParameters parameters, string path)
    {
        if (kind == "decimal" && (parameters.Precision is null || parameters.Scale is null))
            Error(path, "Decimal schemas require explicit precision and scale; no precision is inferred.");
        if (kind == "decimal" && format is "spark-json" or "iceberg-json" or "paimon-json" && parameters.Precision > 38)
            Error(path, "This target supports at most 38 digits of decimal precision.");
    }

    private void ValidateTemporal(string kind, TypeParameters parameters, string path)
    {
        if (kind is not ("timestamp" or "datetime" or "time")) return;
        ValidateTemporalPrecision(parameters, path);
        ReportTimezoneLoss(parameters, path);
        if (format == "spark-json") ValidateSparkTemporal(kind, parameters, path);
    }

    private void ValidateTemporalPrecision(TypeParameters parameters, string path)
    {
        if (format != "paimon-json" && parameters.TemporalPrecision is not null && parameters.TemporalPrecision < 6)
            Info("SCHEMA_PRECISION", path, "Microsecond representation retains values but does not enforce a coarser declared temporal precision.");
        if (parameters.TemporalPrecision > 6) Error(path, "This target retains at most six fractional second digits.");
    }

    private void ReportTimezoneLoss(TypeParameters parameters, string path)
    {
        if (parameters.Timezone is not null || parameters.TemporalSemantics == TemporalSemantics.Offset)
            Info("SCHEMA_TIMEZONE", path, "The target retains the instant, not the original offset or named timezone.");
    }

    private void ValidateSparkTemporal(string kind, TypeParameters parameters, string path)
    {
        if (kind == "time" || parameters.TemporalSemantics == TemporalSemantics.Local)
            Error(path, "Spark 3.x portable JSON export does not support time-only or local timestamp values. Declare instant semantics for timestamps.");
    }

    private void ValidateShape(string kind, DataField field, string path)
    {
        if (kind == "object" && field.Children.Count == 0) Error(path, "An opaque object has no declared record fields; supply a nested schema.");
        if (kind == "map" && field.Map is null) Error(path, "The canonical map has no explicit portable key/value contract. Map keys must be non-null strings; supply an explicit supported shape before export.");
    }

    private string Integer(FieldTypeResolution type)
    {
        var width = type.Parameters.IntegerWidth ?? 32;
        var signed = type.Parameters.Signed ?? true;
        if (!signed && width == 64) { Error(type.FieldPath, "Unsigned 64-bit integers cannot be represented losslessly by Avro/Spark signed integers."); return "long"; }
        var bits = signed ? width : width + 1;
        var kind = format is "spark-json" or "paimon-json" ? SparkInteger(bits) : (bits <= 32 ? "int" : "long");
        if (!signed || (format is "avro" or "iceberg-json" && width < 32))
            Info("SCHEMA_RANGE", type.FieldPath, "The target integer domain is wider than the source; source range constraints are not enforced.");
        return kind;
    }

    private static string SparkInteger(int bits) => bits switch { <= 8 => "byte", <= 16 => "short", <= 32 => "integer", _ => "long" };

    private JObject SparkStruct(IReadOnlyList<Field> fields, JObject? schemaMetadata = null)
    {
        CheckFields(fields, avro: false);
        return new() { ["type"] = "struct", ["fields"] = new JArray(fields.Select((f, index) => new JObject {
            ["name"] = f.Name, ["type"] = SparkType(f), ["nullable"] = f.Nullable,
            ["metadata"] = SparkMetadata(f.Metadata, index == 0 ? schemaMetadata : null) })) };
    }
    private static JObject SparkMetadata(JObject field, JObject? schema)
    {
        var envelope = SchemaConstraintMetadata.Envelope(field, schema, spark: true);
        return envelope.HasValues ? new JObject { ["packata"] = envelope } : new();
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

    private JObject AvroRecord(string proposedName, IReadOnlyList<Field> fields, JObject? schemaMetadata = null)
    {
        var name = UniqueTypeName(proposedName);
        CheckFields(fields, avro: true);
        var record = new JObject { ["type"] = "record", ["name"] = name, ["namespace"] = options?.Namespace ?? "packata.generated",
            ["fields"] = new JArray(fields.Select(f => {
                var field = new JObject { ["name"] = Name(f.Name), ["type"] = AvroType(f, name + "_" + Name(f.Name)) };
                var metadata = SchemaConstraintMetadata.Envelope(f.Metadata, null, spark: false);
                if (metadata.HasValues) field["packata"] = metadata;
                if (f.Description is not null) field["doc"] = f.Description;
                return field;
            })) };
        if (schemaMetadata is { HasValues: true }) record["packata"] = new JObject { ["version"] = 1, ["schema"] = schemaMetadata.DeepClone() };
        return record;
    }
    private JToken AvroType(Field field, string name)
    {
        token.ThrowIfCancellationRequested();
        JToken type = field.Kind switch {
            "string" => AvroString(field, name),
            "object" => AvroRecord(name, field.Children),
            "map" when MapSupported(field) => new JObject { ["type"] = "map", ["values"] = AvroType(field.MapValue!, name + "_value") },
            "array" when field.Items is not null => new JObject { ["type"] = "array", ["items"] = AvroType(field.Items, name + "_item") },
            "decimal" => new JObject { ["type"] = "bytes", ["logicalType"] = "decimal", ["precision"] = field.Parameters.Precision, ["scale"] = field.Parameters.Scale },
            "date" => Logical("int", "date"),
            "time" => Logical("long", "time-micros"),
            "datetime" or "timestamp" => Logical("long", (field.Parameters.TemporalSemantics == TemporalSemantics.Local ? "local-timestamp" : "timestamp") + "-micros"),
            "boolean" or "int" or "long" or "float" or "double" => new JValue(field.Kind),
            _ => Unsupported(field)
        };
        return field.Nullable ? new JArray("null", type) : type;
    }
    private JToken AvroString(Field field, string name)
        => field.EnumSymbols is null ? new JValue("string") : new JObject {
            ["type"] = "enum", ["name"] = UniqueTypeName(name + "_enum"),
            ["namespace"] = options?.Namespace ?? "packata.generated", ["symbols"] = new JArray(field.EnumSymbols) };

    private bool MapSupported(Field field)
    {
        if (field.MapKey is { Kind: "string", Nullable: false } && field.MapValue is not null) return true;
        Error(field.Path, "Portable map exports require an explicit non-null string key and declared value schema.");
        return false;
    }
    private static JObject Logical(string physical, string logical) => new() { ["type"] = physical, ["logicalType"] = logical };
    private JToken Unsupported(Field field) { Error(field.Path, $"Logical type '{field.Kind}' has no supported {format} representation."); return new JValue("string"); }
    private string UniqueTypeName(string proposedName)
    {
        var original = Name(proposedName);
        var name = original;
        var suffix = 2;
        while (!recordNames.Add(name)) name = original + "_" + suffix++;
        return name;
    }

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
            if (avro && name != field.Name) Info("SCHEMA_NAME", field.Path, $"Avro field name normalized to '{name}'.");
            if (!names.Add(name)) Error(field.Path, "Field names collide in the target schema. Rename the conflicting fields explicitly.");
        }
    }
    private void Error(string path, string message) => diagnostics.Add(new("SCHEMA_MAPPING", "Error", path, message, format));
    private void Info(string code, string path, string message) => diagnostics.Add(SchemaConstraintDiagnostics.Limit(code, path, message, format));
}
