using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

/// <summary>Versioned residual metadata. Spark payloads are JSON strings to support arbitrary constraint values.</summary>
internal static class SchemaConstraintMetadata
{
    internal static JObject Schema(DataAsset asset, string? nativeName) => Compact(new()
    {
        ["assetId"] = asset.Id == nativeName ? null : asset.Id,
        ["assetName"] = asset.Name == nativeName || asset.Name == asset.Id ? null : asset.Name,
        ["primaryKey"] = new JArray(asset.Schema!.PrimaryKey),
        ["relationships"] = new JArray(asset.Schema.Relationships.Select(r => Compact(new JObject {
            ["kind"] = r.Kind, ["name"] = r.Name, ["fields"] = new JArray(r.Fields),
            ["targetAsset"] = r.TargetAsset, ["targetFields"] = new JArray(r.TargetFields) }))),
        ["allowAdditionalProperties"] = asset.Schema.AllowAdditionalProperties ? null : new JValue(false),
        ["enforcePropertyOrder"] = asset.Schema.EnforcePropertyOrder ? new JValue(true) : null
    });

    internal static JObject Field(DataField field, string? nativeName, NativeSchemaMetadata native, bool primaryKey)
        => Compact(new()
        {
            ["name"] = field.Name == nativeName ? null : field.Name,
            ["required"] = nativeName is not null && !field.Required ? false : null,
            ["description"] = native.Format == "spark-json" || nativeName is null ? field.Description : null,
            ["format"] = native.RepresentsFormat(field.Format) ? null : field.Format,
            ["physicalType"] = field.PhysicalType,
            ["constraints"] = new JArray(field.Constraints.Where(c => !Represented(c, field, native, primaryKey))
                .Select(c => new JObject { ["kind"] = c.Kind, ["value"] = Value(c.Value) })),
            ["valueDomain"] = Domain(field.ValueDomain), ["qualityRules"] = Quality(Value(field.QualityRules))
        });

    private static bool Represented(DataConstraint constraint, DataField field, NativeSchemaMetadata native, bool primaryKey)
    {
        if (native.Represents(constraint)) return true;
        if (NativeSchemaMetadata.RepeatedByIntegerFormat(constraint, field.Format)) return true;
        if (constraint.Kind.Equals("primaryKey", StringComparison.OrdinalIgnoreCase) && primaryKey)
            return constraint.Value is true;
        return constraint.Kind.Equals("required", StringComparison.OrdinalIgnoreCase) && Equals(constraint.Value, field.Required);
    }

    internal static JObject Envelope(JObject field, JObject? schema, bool spark)
    {
        if (!field.HasValues && schema is not { HasValues: true }) return new();
        var metadata = new JObject { ["version"] = 1 };
        if (field.HasValues) metadata["field"] = Encode(field, spark);
        if (schema is { HasValues: true }) metadata["schema"] = Encode(schema, spark);
        return metadata;
    }

    private static JToken Encode(JObject value, bool spark)
        => spark ? new JValue(value.ToString(Formatting.None)) : value.DeepClone();

    private static JToken Value(object? value)
        => value is null ? JValue.CreateNull() : JToken.FromObject(value);

    // Compact only known metadata containers, never arbitrary user values or membership arrays.
    private static JObject Compact(JObject value)
    {
        foreach (var property in value.Properties().Where(p => p.Value is JValue { Value: null } || p.Value is JContainer { HasValues: false } || (p.Value.Type == JTokenType.String && p.Value.Value<string>() == string.Empty)).ToArray())
            property.Remove();
        return value;
    }

    private static JToken Quality(JToken token)
    {
        if (token is JArray array) foreach (var item in array) Quality(item);
        return token is JObject obj ? QualityObject(obj) : token;
    }

    private static JObject QualityObject(JObject obj)
    {
        foreach (var property in obj.Properties())
        {
            // Typed operands, arguments and extension dictionaries may contain meaningful null/empty data.
            if (!IsQualityData(property.Name)) Quality(property.Value);
        }
        var values = obj.Properties().Where(p => IsQualityData(p.Name)).ToArray();
        foreach (var value in values) value.Remove();
        Compact(obj);
        foreach (var value in values.Where(p => p.Name == "Value" || p.Value is not JContainer { HasValues: false })) obj.Add(value);
        return obj;
    }

    private static bool IsQualityData(string name) => name is "Value" or "Values" or "Arguments" or "Bindings";

    private static JToken Reference(ValueDomainReference? reference)
    {
        if (reference is null) return JValue.CreateNull();
        return Compact(new JObject { ["resourceName"] = reference.ResourceName, ["encodedAs"] = reference.EncodedAs?.ToString(),
            ["boundAssetId"] = reference.BoundAssetId,
            ["package"] = reference.Package is null ? null : new JObject { ["kind"] = reference.Package.Kind.ToString(), ["value"] = reference.Package.Value } });
    }

    private static JObject Entry(ValueOption entry)
    {
        var metadata = Compact(new JObject { ["label"] = entry.Label, ["description"] = entry.Description });
        metadata["value"] = Value(entry.Value.Value);
        return metadata;
    }

    private static JToken Domain(ValueDomain? domain)
    {
        if (domain is null) return JValue.CreateNull();
        var metadata = domain.Entries is { } entries && entries.All(e => e.Value.Value is string)
            ? StringCatalog(entries, domain.AllowedValues!) : TypedCatalog(domain);
        metadata["ordered"] = domain.Ordered;
        metadata["reference"] = Reference(domain.Reference);
        return Compact(metadata);
    }

    private static JObject StringCatalog(IReadOnlyList<ValueOption> entries, IReadOnlyList<ScalarValue> allowedValues)
        => Compact(new JObject
        {
            ["labels"] = Annotations(entries, e => e.Label),
            ["descriptions"] = Annotations(entries, e => e.Description),
            // Membership supplies the catalog only when both its values and declaration order match.
            ["entries"] = entries.Select(e => e.Value).SequenceEqual(allowedValues) ? null
                : new JArray(entries.Select(e => new JObject { ["value"] = Value(e.Value.Value) }))
        });

    private static JObject Annotations(IReadOnlyList<ValueOption> entries, Func<ValueOption, string?> annotation)
    {
        var values = new JObject();
        foreach (var entry in entries)
        {
            var text = annotation(entry);
            if (!string.IsNullOrEmpty(text)) values.Add((string)entry.Value.Value!, text);
        }
        return values;
    }

    private static JObject TypedCatalog(ValueDomain domain)
    {
        // Non-string scalar codes remain typed; JSON object keys could conflate 1 with "1" or null with "null".
        var entries = domain.Entries;
        if (entries is not null && entries.All(e => string.IsNullOrEmpty(e.Label) && string.IsNullOrEmpty(e.Description))
            && entries.Select(e => e.Value).SequenceEqual(domain.AllowedValues!)) entries = null;
        return Compact(new JObject { ["entries"] = entries is null ? null : new JArray(entries.Select(Entry)) });
    }
}
