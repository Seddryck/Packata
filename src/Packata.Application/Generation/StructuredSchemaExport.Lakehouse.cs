using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal sealed partial class StructuredSchemaExport
{
    private const int MaximumFieldId = int.MaxValue - 200;
    private readonly Dictionary<Field, int> fieldIds = new(ReferenceEqualityComparer.Instance);

    private JObject IcebergSchema(DataAsset asset, IReadOnlyList<Field> fields)
    {
        AllocateIds(fields, iceberg: true);
        var root = IcebergStruct(fields);
        root["schema-id"] = MetadataInteger(asset.Extensions, "iceberg", "schema-id", 0, asset.Id, int.MaxValue);
        // Primary keys are descriptive only: identifiers are explicit and do not enforce uniqueness.
        var identifiers = MetadataNames(asset.Extensions, "iceberg", "identifier-fields", asset.Id);
        var ids = new JArray();
        foreach (var name in identifiers)
        {
            var matches = fields.Where(f => f.Name == name).ToArray();
            if (matches.Length != 1) { Error(asset.Id, "Identifier fields must name unambiguous top-level fields."); continue; }
            var field = matches[0];
            if (field.Nullable || !field.Required || field.Kind is "float" or "double" or "object" or "array" or "map")
                Error(field.Path, "Iceberg identifiers must be required non-null primitive fields other than float/double; this exporter supports top-level identifiers only.");
            else ids.Add(fieldIds[field]);
        }
        if (ids.Count > 0)
        {
            root["identifier-field-ids"] = ids;
            Info("SCHEMA_IDENTIFIER", asset.Id, "Iceberg identifier fields describe row identity; they do not enforce uniqueness.");
        }
        return root;
    }

    private JObject IcebergStruct(IReadOnlyList<Field> fields)
    {
        CheckFields(fields, avro: false);
        return new() { ["type"] = "struct", ["fields"] = new JArray(fields.Select(field => {
            var json = new JObject { ["id"] = fieldIds[field], ["name"] = field.Name, ["required"] = !field.Nullable, ["type"] = IcebergType(field) };
            if (field.Description is not null) json["doc"] = field.Description;
            return json;
        })) };
    }

    private JToken IcebergType(Field field)
    {
        token.ThrowIfCancellationRequested();
        return field.Kind switch {
            "object" => IcebergStruct(field.Children),
            "array" when field.Items is { } item => new JObject { ["type"] = "list", ["element-id"] = fieldIds[item], ["element"] = IcebergType(item), ["element-required"] = !item.Nullable },
            "map" when LakehouseMap(field) => new JObject { ["type"] = "map", ["key-id"] = fieldIds[field.MapKey!], ["key"] = IcebergType(field.MapKey!),
                ["value-id"] = fieldIds[field.MapValue!], ["value"] = IcebergType(field.MapValue!), ["value-required"] = !field.MapValue!.Nullable },
            "decimal" => new JValue($"decimal({field.Parameters.Precision}, {field.Parameters.Scale})"),
            "timestamp" or "datetime" => new JValue(field.Parameters.TemporalSemantics == TemporalSemantics.Local ? "timestamp" : "timestamptz"),
            "time" when field.Parameters.TemporalSemantics is TemporalSemantics.Instant or TemporalSemantics.Offset => Unsupported(field),
            "string" or "boolean" or "int" or "long" or "float" or "double" or "date" or "time" => new JValue(field.Kind),
            _ => Unsupported(field)
        };
    }

    private JObject PaimonSchema(DataAsset asset, IReadOnlyList<Field> fields)
    {
        AllocateIds(fields, iceberg: false);
        var primary = asset.Schema!.PrimaryKey;
        var partitions = MetadataNames(asset.Extensions, "paimon", "partition-keys", asset.Id);
        ValidatePaimonKeys(fields, primary, partitions, asset.Id);
        var requestedOptions = TargetMetadata.Value(asset.Extensions, "paimon", "options");
        if (requestedOptions is not null && requestedOptions is not JObject { Count: 0 })
            Error(asset.Id, "Paimon storage options are not interpreted by this schema-only exporter. Configure storage through supported Paimon APIs.");
        var result = new JObject { ["version"] = 3, ["id"] = MetadataInteger(asset.Extensions, "paimon", "schema-id", 0, asset.Id, int.MaxValue),
            ["fields"] = PaimonFields(fields), ["highestFieldId"] = fieldIds.Values.DefaultIfEmpty(-1).Max(),
            ["partitionKeys"] = new JArray(partitions), ["primaryKeys"] = new JArray(primary), ["options"] = new JObject(), ["timeMillis"] = 0 };
        if (asset.Description is not null) result["comment"] = asset.Description;
        return result;
    }

    private JArray PaimonFields(IReadOnlyList<Field> fields)
    {
        CheckFields(fields, avro: false);
        return new(fields.Select(field => {
            var result = new JObject { ["id"] = fieldIds[field], ["name"] = field.Name, ["type"] = PaimonType(field) };
            if (field.Description is not null) result["description"] = field.Description;
            return result;
        }));
    }

    private JToken PaimonType(Field field)
    {
        token.ThrowIfCancellationRequested();
        var suffix = field.Nullable ? "" : " NOT NULL";
        if (field.Kind == "object") return new JObject { ["type"] = "ROW" + suffix, ["fields"] = PaimonFields(field.Children) };
        if (field.Kind == "array" && field.Items is { } item) return new JObject { ["type"] = "ARRAY" + suffix, ["element"] = PaimonType(item) };
        if (field.Kind == "map" && LakehouseMap(field)) return new JObject { ["type"] = "MAP" + suffix, ["key"] = PaimonType(field.MapKey!), ["value"] = PaimonType(field.MapValue!) };
        var type = field.Kind switch {
            "string" => "STRING", "boolean" => "BOOLEAN", "byte" => "TINYINT", "short" => "SMALLINT", "integer" => "INT", "long" => "BIGINT",
            "float" => "FLOAT", "double" => "DOUBLE", "date" => "DATE",
            "decimal" => $"DECIMAL({field.Parameters.Precision}, {field.Parameters.Scale})",
            "time" when field.Parameters.TemporalSemantics is not (TemporalSemantics.Instant or TemporalSemantics.Offset) => $"TIME({field.Parameters.TemporalPrecision ?? 6})",
            "timestamp" or "datetime" => (field.Parameters.TemporalSemantics == TemporalSemantics.Local ? "TIMESTAMP" : "TIMESTAMP_LTZ") + $"({field.Parameters.TemporalPrecision ?? 6})",
            _ => null
        };
        return type is null ? Unsupported(field) : new JValue(type + suffix);
    }

    private bool LakehouseMap(Field field)
    {
        if (field.MapKey is { Nullable: false } && field.MapValue is not null) return true;
        Error(field.Path, "A map requires an explicit non-null key and a declared value schema.");
        return false;
    }

    private void AllocateIds(IReadOnlyList<Field> fields, bool iceberg)
    {
        var nodes = IdFields(fields, iceberg).ToArray();
        var used = new HashSet<int>();
        var target = iceberg ? "iceberg" : "paimon";
        foreach (var field in nodes)
        {
            var supplied = TargetMetadata.Value(field.Source.Extensions, target, "field-id");
            if (supplied is null) continue;
            if (!TryInteger(supplied, out var id) || id < (iceberg ? 1 : 0) || id > MaximumFieldId)
                Error(field.Path, $"Invalid or reserved {target} field ID. Expected an integer from {(iceberg ? 1 : 0)} through {MaximumFieldId}.");
            else if (!used.Add(id)) Error(field.Path, $"Duplicate {target} field ID.");
            else fieldIds[field] = id;
        }
        var next = iceberg ? 1 : 0;
        foreach (var field in nodes)
        {
            if (fieldIds.ContainsKey(field)) continue;
            while (used.Contains(next)) next++;
            fieldIds[field] = next;
            used.Add(next++);
        }
    }

    private static IEnumerable<Field> IdFields(IReadOnlyList<Field> fields, bool iceberg, bool named = true)
    {
        foreach (var field in fields)
        {
            if (named || iceberg) yield return field;
            foreach (var child in IdFields(field.Children, iceberg)) yield return child;
            if (field.Items is not null) foreach (var item in IdFields([field.Items], iceberg, named: false)) yield return item;
            if (field.MapKey is not null) foreach (var key in IdFields([field.MapKey], iceberg, named: false)) yield return key;
            if (field.MapValue is not null) foreach (var value in IdFields([field.MapValue], iceberg, named: false)) yield return value;
        }
    }

    private void ValidatePaimonKeys(IReadOnlyList<Field> fields, IReadOnlyList<string> primary, IReadOnlyList<string> partitions, string path)
    {
        if (primary.Distinct(StringComparer.Ordinal).Count() != primary.Count) Error(path, "Paimon primary keys must be distinct.");
        foreach (var key in primary.Concat(partitions).Distinct(StringComparer.Ordinal))
        {
            var matches = fields.Where(f => f.Name == key).ToArray();
            if (matches.Length != 1) { Error(path, "Paimon keys must reference unambiguous top-level fields."); continue; }
            var field = matches[0];
            if (field.Kind is "array" or "map" or "object") Error(field.Path, "This exporter supports primitive Paimon key fields only.");
            if (primary.Contains(key) && (field.Nullable || !field.Required)) Error(field.Path, "Paimon primary keys must be required and non-null; export does not weaken the contract.");
        }
        if (primary.Count > 0 && partitions.Any(p => !primary.Contains(p))) Error(path, "This exporter requires partition fields to be included in the primary key; cross-partition update policies need explicit runtime configuration.");
        if (primary.Count > 0 && primary.All(partitions.Contains)) Error(path, "Paimon primary keys must include a non-partition field.");
    }

    private int MetadataInteger(ExtensionMetadata metadata, string target, string key, int fallback, string path, int maximum)
    {
        var value = TargetMetadata.Value(metadata, target, key);
        if (value is null) return fallback;
        if (TryInteger(value, out var result) && result >= 0 && result <= maximum) return result;
        Error(path, $"{target}.{key} must be a non-negative integer at most {maximum}.");
        return fallback;
    }

    private static bool TryInteger(JToken value, out int result)
    {
        result = 0;
        return value.Type is JTokenType.Integer or JTokenType.String
            && int.TryParse(value.ToString(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private IReadOnlyList<string> MetadataNames(ExtensionMetadata metadata, string target, string key, string path)
    {
        var value = TargetMetadata.Value(metadata, target, key);
        if (value is null) return [];
        if (value is JArray names && names.All(n => n.Type == JTokenType.String && !string.IsNullOrWhiteSpace(n.Value<string>()))
            && names.Select(n => n.Value<string>()).Distinct(StringComparer.Ordinal).Count() == names.Count) return names.Values<string>().Select(n => n!).ToArray();
        Error(path, $"{target}.{key} must be an array of distinct non-empty field names.");
        return [];
    }

    private void ReportLakehousePolicies(DataAsset asset)
    {
        if (asset.Schema!.Relationships.Count > 0 || (format == "iceberg-json" && asset.Schema.PrimaryKey.Count > 0))
            Info("SCHEMA_RELATIONAL", asset.Id, "This schema artifact does not enforce primary-key uniqueness or relationships. Validate the contract separately.");
        if (!asset.Schema.AllowAdditionalProperties || asset.Schema.EnforcePropertyOrder)
            Info("SCHEMA_PROPERTY_POLICY", asset.Id, "Contract property-presence and order policies require separate validation.");
        ValidateTargetMetadata(asset.Extensions, asset.Id, field: false);
        ValidateTargetMetadata(asset.Schema.Extensions, asset.Id + "/schema", field: false, schema: true);
    }

    private void ReportLakehouseLoss(DataField field, string path)
    {
        if (field.Name.Length == 0) Error(path, "Field names must not be empty.");
        if (!field.Required) Info("SCHEMA_REQUIRED", path, "The target has value nullability, but cannot distinguish absent properties from present values. Canonical requiredness is not changed.");
        foreach (var constraint in field.Constraints.Where(c => !Parameters.Contains(c.Kind) && c.Kind != "required"))
            Info("SCHEMA_CONSTRAINT", path, $"Constraint '{constraint.Kind}' is not enforced by this schema artifact; validate the source contract separately.");
        if (field.PhysicalType is not null) Info("SCHEMA_PHYSICAL", path, "Physical types are not portable; canonical logical parameters are used.");
        if (field.QualityRules.Count > 0) Info("SCHEMA_QUALITY", path, "Quality rules require separate contract validation.");
        ValidateTargetMetadata(field.Extensions, path, field: true);
    }

    private void ValidateTargetMetadata(ExtensionMetadata metadata, string path, bool field, bool schema = false)
    {
        var target = format == "iceberg-json" ? "iceberg" : "paimon";
        var supported = field ? new[] { "field-id" } : schema ? [] : target == "iceberg" ? ["schema-id", "identifier-fields"] : ["schema-id", "partition-keys", "options"];
        foreach (var entry in TargetMetadata.Entries(metadata))
        {
            var name = entry.Key.StartsWith("packata.", StringComparison.Ordinal) ? entry.Key[8..] : entry.Key;
            if (name.StartsWith(target + ".", StringComparison.Ordinal) && !supported.Contains(name[(target.Length + 1)..]))
                Error(path, $"Unsupported {target} metadata option '{name}'.");
        }
    }
}
