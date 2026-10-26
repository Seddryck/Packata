using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class CategoryLabelSelection
{
    public static IReadOnlyList<DataField> Resolve(DataAsset asset, string? language, string? label, Action<string>? diagnostic)
    {
        var table = asset.CategoryTable!;
        var fields = EligibleFields(asset);
        if (label is not null)
            return [fields.SingleOrDefault(field => field.Name == label)
                ?? throw Error(asset, $"'{label}' is not a declared label field; check --decode-categories-label")];
        var result = LanguageFields(asset, fields, language);
        if (language is not null && result.Count == 0)
            diagnostic?.Invoke($"Category table '{asset.Name}' has no label for language '{language}'; using " +
                (table.LabelField is null ? "original codes." : "the default label, then original codes."));
        if (table.LabelField is not null) result.Add(Field(asset, table.LabelField));
        return result.DistinctBy(field => field.Name).ToArray();
    }

    private static DataField[] EligibleFields(DataAsset asset)
    {
        var table = asset.CategoryTable!;
        var paths = table.LabelFields.Concat(table.LabelField is null ? [] : new[] { table.LabelField });
        var fields = paths.Select(path => Field(asset, path)).DistinctBy(field => field.Name).ToArray();
        if (fields.Any(field => field.LogicalType != "string"))
            throw Error(asset, "mapped labels must be strings");
        if (fields.Any(field => field.Language is not null && !LanguageTag.IsValid(field.Language)))
            throw Error(asset, "a declared label has an invalid language tag");
        return fields;
    }

    private static List<DataField> LanguageFields(DataAsset asset, DataField[] fields, string? language)
    {
        var result = new List<DataField>();
        if (language is null) return result;
        foreach (var tag in new[] { language, language.Split('-')[0] }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = fields.Where(field => string.Equals(field.Language, tag, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw Error(asset, $"language '{tag}' matches multiple label fields; use --decode-categories-label to select one explicitly");
            if (matches.Length == 1) result.Add(matches[0]);
        }
        return result;
    }

    private static DataField Field(DataAsset asset, FieldPath path)
    {
        var matches = path.Segments.Count == 1
            ? asset.Schema!.Fields.Where(field => field.Name == path.Segments[0]).ToArray() : [];
        return matches.Length == 1 ? matches[0] : throw Error(asset, "mapped columns must name unique top-level fields");
    }

    private static CategoryTableDecodingException Error(DataAsset asset, string message) => new(asset.Name, message);
}
