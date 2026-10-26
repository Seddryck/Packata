using System.Collections;
using System.Text.Json;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

/// <summary>Packata's ODCS convention. Resolves metadata only; data loading belongs to the shared reader.</summary>
internal static class CategoryTableMapper
{
    public static DataAsset[] Map(DataContract document, DataAsset[] assets, ICollection<MappingDiagnostic> diagnostics)
    {
        // First map every table so declarations may reference objects in either source order.
        assets = assets.Select((asset, index) => asset with
        {
            CategoryTable = Try(() => Table(document.Schema[index], asset), $"schema/{asset.Name}/customProperties", diagnostics)
        }).ToArray();
        return assets.Select((asset, index) => asset with
        {
            Schema = asset.Schema! with { Fields = asset.Schema.Fields.Select((field, fieldIndex) =>
                Field(document.Schema[index].Properties[fieldIndex], field, assets,
                    $"schema/{asset.Name}/properties/{field.Name}", diagnostics)).ToArray() }
        }).ToArray();
    }

    private static CategoryTableDefinition? Table(SchemaObject source, DataAsset asset)
    {
        var property = Declaration(source.CustomProperties, "categoryFieldMap");
        if (property is null) return null;
        if (asset.Kind != AssetKind.Table) throw Invalid("categoryFieldMap requires a table schema object.");
        var map = Object(property.Value);
        var value = Text(map, "value", required: true)!;
        var label = Text(map, "label");
        var ordered = Ordered(map);
        ValidateValueColumn(asset, value);
        ValidateLabelColumn(asset, label);
        return new(new(value), label is null ? null : new(label), ordered,
            ExtensionMetadata.For("odcs", new Dictionary<string, object?> { ["categoryFieldMap"] = MetadataSnapshot.CopyValue(map) }));
    }

    private static bool? Ordered(IReadOnlyDictionary<string, object?> map)
    {
        if (!map.TryGetValue("ordered", out var raw)) return null;
        return raw is bool flag ? flag : throw Invalid("categoryFieldMap.ordered must be boolean when present.");
    }

    private static void ValidateValueColumn(DataAsset asset, string value)
    {
        var valueField = Column(asset, value);
        if (valueField.LogicalType is not ("string" or "integer"))
            throw Invalid("The mapped value column must have type string or integer.");
        if (!valueField.Required || !valueField.Constraints.Any(c => c.Kind == "unique" && c.Value is true))
            throw Invalid("The mapped value column must declare required: true and unique: true.");
    }

    private static void ValidateLabelColumn(DataAsset asset, string? label)
    {
        if (label is null) return;
        var labelField = Column(asset, label);
        if (labelField.LogicalType != "string" || !labelField.Constraints.Any(c => c.Kind == "unique" && c.Value is true))
            throw Invalid("The mapped label column must have type string and declare unique: true.");
    }

    private static DataField Field(SchemaProperty source, DataField field, IReadOnlyList<DataAsset> assets,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        // Nested properties retain their declaration and diagnostics even though lookup columns are top-level.
        var children = field.Children.Select((child, index) => Field(source.Properties[index], child, assets,
            $"{path}/properties/{child.Name}", diagnostics)).ToArray();
        var items = source.Items is null || field.Items is null ? field.Items
            : Field(source.Items, field.Items, assets, $"{path}/items", diagnostics);
        var domain = Try(() => Reference(source, field, assets), path + "/customProperties", diagnostics);
        return field with { Children = children, Items = items, ValueDomain = domain ?? field.ValueDomain };
    }

    private static ValueDomain? Reference(SchemaProperty source, DataField field, IReadOnlyList<DataAsset> assets)
    {
        var property = Declaration(source.CustomProperties, "categories");
        if (property is null) return null;
        if (source.Enum is not null) throw Invalid("Inline enum and category-table reference declarations conflict.");
        if (field.LogicalType is not ("string" or "integer"))
            throw Invalid("Category-table references require a string or integer property.");
        var map = Object(property.Value);
        var resource = Text(map, "resource", required: true)!;
        var encoding = Text(map, "encodedAs") switch
        {
            null => (ValueEncoding?)null, "value" => ValueEncoding.Value, "label" => ValueEncoding.Label,
            _ => throw Invalid("categories.encodedAs must be value or label when present.")
        };
        // Retain external references as metadata; the shared resolver reports unsupported references on decoding.
        var packageName = Text(map, "package");
        var package = Package(packageName);
        var bound = package is null ? Bind(assets, resource, field, encoding) : null;
        return new(reference: new(resource, package, encoding, bound));
    }

    private static PackageLocator? Package(string? name)
    {
        if (name is null) return null;
        return Uri.TryCreate(name, UriKind.Absolute, out _)
            ? new(PackageLocatorKind.DescriptorUri, name) : new(PackageLocatorKind.RegistryName, name);
    }

    private static string Bind(IReadOnlyList<DataAsset> assets, string resource, DataField field, ValueEncoding? encoding)
    {
        var targets = assets.Where(asset => asset.Name == resource).ToArray();
        if (targets.Length != 1 || targets[0].CategoryTable is not { } table)
            throw Invalid($"Category table '{resource}' must identify exactly one schema object with a valid categoryFieldMap.");
        var target = targets[0];
        if (Column(target, table.ValueField.Segments.Single()).LogicalType != field.LogicalType)
            throw Invalid($"Property '{field.Name}' must match the mapped value column's logical type.");
        if (encoding == ValueEncoding.Label && table.LabelField is null)
            throw Invalid("Label encoding requires a mapped default label column.");
        return target.Id;
    }

    private static CustomProperty? Declaration(CustomProperties properties, string name)
    {
        var matches = properties.Where(property => property.Vendor == "packata" && property.Property == name).ToArray();
        if (matches.Length > 1) throw Invalid($"Duplicate vendor: packata property: {name} declarations.");
        return matches.SingleOrDefault();
    }

    private static DataField Column(DataAsset asset, string name)
    {
        var matches = asset.Schema!.Fields.Where(field => field.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : throw Invalid($"Mapped column '{name}' must identify one top-level field in '{asset.Name}'.");
    }

    private static Dictionary<string, object?> Object(object? value)
    {
        if (value is JsonElement json) return JsonObject(json);
        if (value is not IDictionary dictionary) throw Invalid("The custom-property value must be a mapping object.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key || !values.TryAdd(key, entry.Value))
                throw Invalid("Category metadata keys must be unique strings.");
        }
        return values;
    }

    private static Dictionary<string, object?> JsonObject(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) throw Invalid("The custom-property value must be a mapping object.");
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var member in json.EnumerateObject()) AddJsonMember(result, member);
        return result;
    }

    private static void AddJsonMember(Dictionary<string, object?> result, JsonProperty member)
    {
        object? value = member.Value.ValueKind switch
        {
            JsonValueKind.String => member.Value.GetString(), JsonValueKind.True => true,
            JsonValueKind.False => false, _ => member.Value.Clone()
        };
        if (!result.TryAdd(member.Name, value)) throw Invalid($"Duplicate category metadata key '{member.Name}'.");
    }

    private static string? Text(IReadOnlyDictionary<string, object?> map, string key, bool required = false)
    {
        if (!map.TryGetValue(key, out var value))
            return required ? throw Invalid($"An explicit '{key}' field is required.") : null;
        return value is string text && !string.IsNullOrWhiteSpace(text) ? text
            : throw Invalid($"'{key}' must be a non-empty string when present.");
    }

    private static T? Try<T>(Func<T?> map, string path, ICollection<MappingDiagnostic> diagnostics) where T : class
    {
        try { return map(); }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new("ODCS007", MappingSeverity.Error, path, exception.Message));
            return null;
        }
    }

    private static ArgumentException Invalid(string message) => new(message);
}
