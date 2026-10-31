using System.Collections;

namespace Packata.Core.Contracts;

internal static class SatisfactionExtensionCoverage
{
    internal static bool HasUnresolved(ExtensionMetadata metadata)
        => metadata.Namespaces.Any(name => metadata[name].Any(pair => HasValue(pair.Value)
            && (name is not ("datapackage" or "odcs") || !Informational(pair.Key, pair.Value))));

    private static bool Informational(string key, object? value) => key switch
    {
        "title" or "description" or "example" or "rdfType" or "homepage" or "sources" or "licenses" or "created" or "image" => true,
        "fieldsMatch" => value is "Exact" or "Equal" or "Subset" or "Superset" or "Partial",
        "customProperties" => value is IEnumerable properties && properties.Cast<object?>().All(IsMappedPropertyRequirement),
        "profile" => value is "https://datapackage.org/profiles/1.0/datapackage.json"
            or "https://datapackage.org/profiles/1.0/dataresource.json" or "tabular-data-package" or "tabular-data-resource" or "data-package" or "data-resource",
        _ => false
    };

    internal static bool IsMappedPropertyRequirement(object? value) => value is IReadOnlyDictionary<string, object?> property
        && property.GetValueOrDefault("vendor") is "packata"
        && property.Keys.All(key => key is "vendor" or "property" or "value" or "id" or "description")
        && (property.GetValueOrDefault("property"), property.GetValueOrDefault("value")) is
            ("propertyMatching", "equal" or "subset" or "superset" or "partial") or ("propertyOrder", "any" or "declared");

    private static bool HasValue(object? value)
    {
        if (value is null) return false;
        if (value is IEnumerable sequence and not string) return sequence.Cast<object?>().Any();
        return true;
    }
}
