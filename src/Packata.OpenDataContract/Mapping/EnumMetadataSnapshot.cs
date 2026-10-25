using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static class EnumMetadataSnapshot
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Copy(IEnumerable<EnumerationValue> entries)
        => Array.AsReadOnly(entries.Select(Entry).ToArray());

    public static IReadOnlyDictionary<string, object?> Entry(EnumerationValue entry)
        => MetadataSnapshot.CopyDictionary(new Dictionary<string, object?>
        {
            ["value"] = entry.Value, ["label"] = entry.Label, ["description"] = entry.Description,
            ["id"] = entry.Id, ["tags"] = entry.Tags,
            ["customProperties"] = CustomProperties(entry.CustomProperties),
            ["authoritativeDefinitions"] = entry.AuthoritativeDefinitions.Select(definition => new Dictionary<string, object?>
            {
                ["url"] = definition.Url, ["type"] = definition.Type, ["description"] = definition.Description,
                ["customProperties"] = CustomProperties(definition.CustomProperties)
            }).ToArray()
        });

    private static object CustomProperties(CustomProperties properties) => properties.Select(property => new Dictionary<string, object?>
    {
        ["id"] = property.Id, ["property"] = property.Property, ["value"] = property.Value,
        ["description"] = property.Description, ["vendor"] = property.Vendor
    }).ToArray();
}
