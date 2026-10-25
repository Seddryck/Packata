using System.Text.RegularExpressions;

namespace Packata.DataPackage.Validation;

/// <summary>Descriptor-level checks for the Category Tables recipe; never loads category data.</summary>
internal static class CategoryTableValidator
{
    public static void ValidateResource(Resource resource, string path, ICollection<DataPackageValidationIssue> issues)
    {
        var map = resource.CategoryFieldMap;
        if (map is null) return;
        path += ".categoryFieldMap";
        if (!string.Equals(resource.Type, "table", StringComparison.OrdinalIgnoreCase) || resource.Schema is null)
            Add(issues, path, "A category table must be a tabular resource with a schema.");
        var value = FindField(resource, map.Value, path + ".value", issues);
        if (value is not null)
        {
            if (FieldType(value) is not ("string" or "integer"))
                Add(issues, path + ".value", "The category value field must have type string or integer.");
            if (value.Constraints?.Get<RequiredConstraint>()?.Value != true || value.Constraints?.Get<UniqueConstraint>()?.Value != true)
                Add(issues, path + ".value", "The category value field must declare required: true and unique: true.");
        }
        if (map.Label is null) return;
        var label = FindField(resource, map.Label, path + ".label", issues);
        if (label is not null && (FieldType(label) != "string" || label.Constraints?.Get<UniqueConstraint>()?.Value != true))
            Add(issues, path + ".label", "The category label field must have type string and declare unique: true.");
    }

    public static void ValidateReferences(Schema schema, string path, DataPackage package, ICollection<DataPackageValidationIssue> issues)
    {
        for (var index = 0; index < schema.Fields.Count; index++)
        {
            var field = schema.Fields[index];
            if (field.Categories?.Reference is not { } reference) continue;
            var referencePath = $"{path}.fields[{index}].categories";
            if (FieldType(field) is not ("string" or "integer"))
                Add(issues, referencePath, "Category table references are only defined for string and integer fields.");
            if (string.IsNullOrWhiteSpace(reference.Resource))
                Add(issues, referencePath + ".resource", "A category table resource name is required.");
            if (reference.EncodedAs is not (null or "value" or "label"))
                Add(issues, referencePath + ".encodedAs", "encodedAs must be value or label when present.");
            if (reference.Package is not null)
            {
                if (!IsPackageReference(reference.Package))
                    Add(issues, referencePath + ".package", "Expected an HTTP(S) package descriptor URL ending in .json or a package registry name.");
                continue; // External resources are metadata, not local references. No network access.
            }
            if (string.IsNullOrWhiteSpace(reference.Resource)) continue;
            var target = package.Resources.FirstOrDefault(item => item.Name == reference.Resource);
            if (target is null)
            { Add(issues, referencePath + ".resource", $"Category table resource '{reference.Resource}' does not exist in this package."); continue; }
            if (!string.Equals(target.Type, "table", StringComparison.OrdinalIgnoreCase) || target.Schema is null || target.CategoryFieldMap is null)
            { Add(issues, referencePath + ".resource", "The referenced resource must be a category table with schema and categoryFieldMap."); continue; }
            var value = target.Schema.Fields.FirstOrDefault(item => item.Name == target.CategoryFieldMap.Value);
            // Follow the merged recipe's explicit type rule even when encodedAs is label.
            if (value is not null && FieldType(field) != FieldType(value))
                Add(issues, referencePath, "The referencing field type must match the category table value field type.");
            if (reference.EncodedAs == "label" && target.CategoryFieldMap.Label is null)
                Add(issues, referencePath + ".encodedAs", "encodedAs: label requires a label field in categoryFieldMap.");
        }
    }

    private static Field? FindField(Resource resource, string? name, string path, ICollection<DataPackageValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name)) { Add(issues, path, "A mapped field name is required."); return null; }
        var field = resource.Schema?.Fields.FirstOrDefault(item => item.Name == name);
        if (field is null) Add(issues, path, $"Mapped field '{name}' does not exist in the category table schema.");
        return field;
    }
    private static string FieldType(Field field) => field.Type ?? (field is StringField ? "string" : field is IntegerField ? "integer" : "any");
    private static bool IsPackageReference(string value)
        => Regex.IsMatch(value, @"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)
            || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    private static void Add(ICollection<DataPackageValidationIssue> issues, string path, string message) => issues.Add(new(path, message));
}
