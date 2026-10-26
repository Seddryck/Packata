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
        ValidateValue(resource, map, path, issues);
        ValidateLabels(resource, map, path, issues);
        foreach (var field in (resource.Schema?.Fields ?? []).Where(field =>
            field.Language is not null && !Packata.Core.Contracts.LanguageTag.IsValid(field.Language)))
            Add(issues, path, $"Field '{field.Name}' has an invalid language tag '{field.Language}'.");
    }

    private static void ValidateValue(Resource resource, CategoryFieldMap map, string path, ICollection<DataPackageValidationIssue> issues)
    {
        var value = FindField(resource, map.Value, path + ".value", issues);
        if (value is not null)
        {
            if (!FieldTypeNames.SupportsCategories(FieldTypeNames.Get(value)))
                Add(issues, path + ".value", "The category value field must have type string or integer.");
            if (value.Constraints?.Get<RequiredConstraint>()?.Value != true || value.Constraints?.Get<UniqueConstraint>()?.Value != true)
                Add(issues, path + ".value", "The category value field must declare required: true and unique: true.");
        }
    }

    private static void ValidateLabels(Resource resource, CategoryFieldMap map, string path, ICollection<DataPackageValidationIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in map.Labels ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                Add(issues, path + ".labels", "Label field names must be non-empty and unique.");
            var alternate = FindField(resource, name, path + ".labels", issues);
            if (alternate is not null && FieldTypeNames.Get(alternate) != FieldTypeNames.String)
                Add(issues, path + ".labels", "Category label fields must have type string.");
        }
        ValidateDefaultLabel(resource, map.Label, path, issues);
    }

    private static void ValidateDefaultLabel(Resource resource, string? name, string path, ICollection<DataPackageValidationIssue> issues)
    {
        if (name is null) return;
        var label = FindField(resource, name, path + ".label", issues);
        if (label is not null && (FieldTypeNames.Get(label) != FieldTypeNames.String || label.Constraints?.Get<UniqueConstraint>()?.Value != true))
            Add(issues, path + ".label", "The category label field must have type string and declare unique: true.");
    }

    public static void ValidateReferences(Schema schema, string path, DataPackage package, ICollection<DataPackageValidationIssue> issues)
    {
        for (var index = 0; index < schema.Fields.Count; index++)
        {
            var field = schema.Fields[index];
            if (field.Categories?.Reference is { } reference)
                ValidateReference(field, reference, $"{path}.fields[{index}].categories", package, issues);
        }
    }

    private static void ValidateReference(Field field, CategoryTableReference reference, string path,
        DataPackage package, ICollection<DataPackageValidationIssue> issues)
    {
        ValidateReferenceShape(field, reference, path, issues);
        if (reference.Package is not null)
        {
            if (!PackageReferenceSyntax.IsValid(reference.Package))
                Add(issues, path + ".package", "Expected an HTTP(S) package descriptor URL ending in .json or a package registry name.");
            return; // External resources are metadata, not local references. No network access.
        }
        ValidateLocalReference(field, reference, path, package, issues);
    }

    private static void ValidateReferenceShape(Field field, CategoryTableReference reference, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (!FieldTypeNames.SupportsCategories(FieldTypeNames.Get(field)))
            Add(issues, path, "Category table references are only defined for string and integer fields.");
        if (string.IsNullOrWhiteSpace(reference.Resource))
            Add(issues, path + ".resource", "A category table resource name is required.");
        if (reference.EncodedAs is not (null or "value" or "label"))
            Add(issues, path + ".encodedAs", "encodedAs must be value or label when present.");
    }

    private static void ValidateLocalReference(Field field, CategoryTableReference reference, string path,
        DataPackage package, ICollection<DataPackageValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(reference.Resource)) return;
        var target = package.Resources.FirstOrDefault(item => item.Name == reference.Resource);
        if (target is null)
        {
            Add(issues, path + ".resource", $"Category table resource '{reference.Resource}' does not exist in this package.");
            return;
        }
        if (!string.Equals(target.Type, "table", StringComparison.OrdinalIgnoreCase) || target.Schema is null || target.CategoryFieldMap is null)
        {
            Add(issues, path + ".resource", "The referenced resource must be a category table with schema and categoryFieldMap.");
            return;
        }
        var value = target.Schema.Fields.FirstOrDefault(item => item.Name == target.CategoryFieldMap.Value);
        // Follow the merged recipe's explicit type rule even when encodedAs is label.
        if (value is not null && FieldTypeNames.Get(field) != FieldTypeNames.Get(value))
            Add(issues, path, "The referencing field type must match the category table value field type.");
        if (reference.EncodedAs == "label" && target.CategoryFieldMap.Label is null)
            Add(issues, path + ".encodedAs", "encodedAs: label requires a label field in categoryFieldMap.");
    }

    private static Field? FindField(Resource resource, string? name, string path, ICollection<DataPackageValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name)) { Add(issues, path, "A mapped field name is required."); return null; }
        var field = resource.Schema?.Fields.FirstOrDefault(item => item.Name == name);
        if (field is null) Add(issues, path, $"Mapped field '{name}' does not exist in the category table schema.");
        return field;
    }
    private static void Add(ICollection<DataPackageValidationIssue> issues, string path, string message) => issues.Add(new(path, message));
}
