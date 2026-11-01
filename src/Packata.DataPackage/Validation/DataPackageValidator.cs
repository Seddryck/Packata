using System.Text.RegularExpressions;

namespace Packata.DataPackage.Validation;

/// <summary>Validates a complete Data Package v2 descriptor and reports JSON-path diagnostics.</summary>
public static class DataPackageValidator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> FieldTypes =
    [
        FieldTypeNames.String, "number", FieldTypeNames.Integer, "date", "time", "datetime", "year", "yearmonth",
        "boolean", "object", "geopoint", "geojson", "array", "duration", "any"
    ];

    public static DataPackageValidationResult Validate(DataPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var issues = new List<DataPackageValidationIssue>();
        if (package.Resources.Count == 0)
            Add(issues, "$.resources", "A data package must contain at least one resource.");

        ValidateContributors(package.Contributors, "$.contributors", issues);
        ValidateLicenses(package.Licenses, "$.licenses", issues);
        ValidateSources(package.Sources, "$.sources", issues);

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < package.Resources.Count; index++)
        {
            var resource = package.Resources[index];
            var path = $"$.resources[{index}]";
            if (string.IsNullOrWhiteSpace(resource.Name))
                Add(issues, $"{path}.name", "A resource name is required.");
            else if (!names.Add(resource.Name))
                Add(issues, $"{path}.name", $"Resource name '{resource.Name}' must be unique within the package.");
            ValidateResource(resource, path, package, issues);
        }

        return new DataPackageValidationResult(issues);
    }

    public static bool IsValid(DataPackage package) => Validate(package).IsValid;

    private static void ValidateResource(Resource resource, string path, DataPackage package,
        ICollection<DataPackageValidationIssue> issues)
    {
        ValidateResourceLocation(resource, path, issues);
        ValidateResourceType(resource, path, issues);
        ValidateResourcePaths(resource, path, issues);
        if (resource.Bytes < 0)
            Add(issues, $"{path}.bytes", "Resource bytes cannot be negative.");

        ValidateSources(resource.Sources, $"{path}.sources", issues);
        ValidateLicenses(resource.Licenses, $"{path}.licenses", issues);
        ValidateSchema(resource.Schema, $"{path}.schema", resource, package, issues);
        CategoryTableValidator.ValidateResource(resource, path, issues);
        ValidateDialect(resource.Dialect, $"{path}.dialect", issues);
    }

    private static void ValidateResourceLocation(Resource resource, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var hasPaths = resource.Paths.Count > 0;
        var hasData = resource.Data is not null;
        if (hasPaths && hasData)
            Add(issues, path, "A resource cannot define both path and data.");
        if (string.Equals(resource.Type, "table", StringComparison.OrdinalIgnoreCase) && !hasPaths && !hasData)
            Add(issues, path, "A tabular resource must define path or data.");
    }

    private static void ValidateResourceType(Resource resource, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (resource.Type is not null && !resource.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
            Add(issues, $"{path}.type", "The only standard Data Package v2 resource type is 'table'.");
    }

    private static void ValidateResourcePaths(Resource resource, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var fullyQualified = resource.Paths.Select(item => item.IsFullyQualified).Distinct().ToArray();
        if (fullyQualified.Length > 1)
            Add(issues, $"{path}.path", "A path array cannot mix relative paths and fully qualified URLs.");
    }

    private static void ValidateSchema(Schema? schema, string path, Resource resource, DataPackage package,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (schema is null) return;
        var fields = ValidateFields(schema.Fields, $"{path}.fields", issues);
        CategoryTableValidator.ValidateReferences(schema, path, package, issues);
        ValidateKey(schema.PrimaryKey, $"{path}.primaryKey", fields, issues);
        ValidateUniqueKeys(schema.UniqueKeys, path, fields, issues);
        ValidateForeignKeys(schema.ForeignKeys, path, fields, resource, package, issues);
    }

    private static HashSet<string> ValidateFields(IReadOnlyList<Field> schemaFields, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (schemaFields.Count == 0)
            Add(issues, path, "A table schema must contain at least one field.");

        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < schemaFields.Count; index++)
        {
            var field = schemaFields[index];
            var fieldPath = $"{path}[{index}]";
            ValidateField(field, fieldPath, fields, issues);
        }
        return fields;
    }

    private static void ValidateField(Field field, string fieldPath, HashSet<string> fields,
        ICollection<DataPackageValidationIssue> issues)
    {
        ValidateFieldName(field.Name, fieldPath, fields, issues);
        ValidateFieldAnnotations(field, fieldPath, issues);
        if (field.Type is not null && !FieldTypes.Contains(field.Type))
            Add(issues, $"{fieldPath}.type", $"Field type '{field.Type}' is not defined by Data Package v2.");
        ValidateCategories(field, fieldPath, issues);
    }

    private static void ValidateFieldAnnotations(Field field, string fieldPath, ICollection<DataPackageValidationIssue> issues)
    {
        if (field.Language is not null && !Packata.Core.Contracts.LanguageTag.IsValid(field.Language))
            Add(issues, $"{fieldPath}.language", "Expected a language tag such as fr or fr-BE.");
        if (field.CategoriesOrdered is not null && field.Categories is null)
            Add(issues, $"{fieldPath}.categoriesOrdered", "categoriesOrdered requires categories.");
    }

    private static void ValidateFieldName(string? name, string fieldPath, HashSet<string> fields,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name))
            Add(issues, $"{fieldPath}.name", "A field name is required.");
        else if (!fields.Add(name))
            Add(issues, $"{fieldPath}.name", $"Field name '{name}' must be unique within the schema.");
    }

    private static void ValidateCategories(Field field, string path, ICollection<DataPackageValidationIssue> issues)
    {
        if (field.Categories is null || field.Categories.Reference is not null) return;
        var type = FieldTypeNames.Get(field);
        if (!FieldTypeNames.SupportsCategories(type))
            Add(issues, $"{path}.categories", "Categories are only defined for string and integer fields.");
        var values = CollectCategoryValues(field.Categories, type, path, issues);
        ValidateCategorySubset(field.Constraints?.Get<EnumConstraint>(), values, path, issues);
    }

    private static HashSet<object> CollectCategoryValues(CategoryCollection categories, string type, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var values = new HashSet<object>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        bool? objects = null;
        for (var index = 0; index < categories.Count; index++)
        {
            var itemPath = $"{path}.categories[{index}]";
            var objectForm = ValidateCategory(categories[index], type, itemPath, values, labels, issues);
            if (objectForm is null) continue;
            if (objects is not null && objects != objectForm)
                Add(issues, itemPath, "Categories must contain either scalar values or objects, not both.");
            objects = objectForm;
        }
        return values;
    }

    private static bool? ValidateCategory(ICategory? category, string type, string path, ISet<object> values,
        ISet<string> labels, ICollection<DataPackageValidationIssue> issues)
    {
        if (category is null || !CategoryValues.TryNormalize(category.Value, out var value))
        {
            Add(issues, path, "A category value must be a string or signed 64-bit integer.");
            return null;
        }
        if (!MatchesCategoryType(value!, type))
            Add(issues, path, "Category values must match the field's logical type.");
        if (!values.Add(value!)) Add(issues, path, "Category values must be unique.");
        var objectForm = category is Category;
        if (objectForm && category.Label is not null && !labels.Add(category.Label))
            Add(issues, $"{path}.label", "Category labels must be unique when present.");
        return objectForm;
    }

    private static bool MatchesCategoryType(object value, string type) => type switch
    {
        FieldTypeNames.Integer => value is long,
        FieldTypeNames.String => value is string,
        _ => true // The field-level diagnostic reports unsupported category field types.
    };

    private static void ValidateCategorySubset(EnumConstraint? enumeration, ISet<object> values, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (enumeration is null) return;
        for (var index = 0; index < enumeration.Value.Count; index++)
        {
            if (!CategoryValues.TryNormalize(enumeration.Value[index], out var value) || !values.Contains(value!))
                Add(issues, $"{path}.constraints.enum[{index}]", "Enum values must be a subset of categories with matching logical types.");
        }
    }

    private static void ValidateUniqueKeys(IReadOnlyList<List<string>>? uniqueKeys, string path,
        ISet<string> fields, ICollection<DataPackageValidationIssue> issues)
    {
        if (uniqueKeys is null) return;
        for (var index = 0; index < uniqueKeys.Count; index++)
            ValidateKey(uniqueKeys[index], $"{path}.uniqueKeys[{index}]", fields, issues);
    }

    private static void ValidateForeignKeys(IReadOnlyList<ForeignKey>? foreignKeys, string path,
        ISet<string> fields, Resource resource, DataPackage package,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (foreignKeys is null) return;
        for (var index = 0; index < foreignKeys.Count; index++)
        {
            var foreignKey = foreignKeys[index];
            var foreignPath = $"{path}.foreignKeys[{index}]";
            ValidateForeignKey(foreignKey, foreignPath, fields, resource, package, issues);
        }
    }

    private static void ValidateForeignKey(ForeignKey foreignKey, string path, ISet<string> fields,
        Resource resource, DataPackage package, ICollection<DataPackageValidationIssue> issues)
    {
        ValidateKey(foreignKey.Fields, $"{path}.fields", fields, issues);
        if (foreignKey.Reference is null)
        {
            Add(issues, $"{path}.reference", "A foreign key reference is required.");
            return;
        }
        if (foreignKey.Fields.Count != foreignKey.Reference.Fields.Count)
            Add(issues, path, "Foreign-key fields and reference fields must have the same length.");

        var target = FindReferencedResource(foreignKey.Reference, resource, package);
        if (target is null)
        {
            Add(issues, $"{path}.reference.resource",
                $"Referenced resource '{foreignKey.Reference.Resource}' does not exist.");
            return;
        }
        var targetFields = target.Schema?.Fields.Where(item => item.Name is not null)
            .Select(item => item.Name!).ToHashSet(StringComparer.Ordinal) ?? [];
        ValidateKey(foreignKey.Reference.Fields, $"{path}.reference.fields", targetFields, issues);
    }

    private static Resource? FindReferencedResource(Reference reference, Resource resource, DataPackage package)
        => string.IsNullOrWhiteSpace(reference.Resource)
            ? resource
            : package.Resources.FirstOrDefault(item => item.Name == reference.Resource);

    private static void ValidateKey(IReadOnlyCollection<string>? key, string path, ISet<string> fields,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (key is null) return;
        if (key.Count == 0) Add(issues, path, "A key must contain at least one field name.");
        if (key.Count != key.Distinct(StringComparer.Ordinal).Count())
            Add(issues, path, "A key cannot contain duplicate field names.");
        foreach (var name in key.Where(name => !fields.Contains(name)))
            Add(issues, path, $"Field '{name}' is not declared in the schema.");
    }

    private static void ValidateDialect(TableDialect? dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect is null) return;
        ValidateDialectHeader(dialect, path, issues);
        ValidateDialectRows(dialect, path, issues);
        ValidateDialectCharacters(dialect, path, issues);
        ValidateSpreadsheetDialect(dialect, path, issues);
        ValidateStructuredDialect(dialect, path, issues);
    }

    private static void ValidateDialectHeader(TableDialect dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect.Header && (dialect.HeaderRows is null || dialect.HeaderRows.Count == 0))
            Add(issues, $"{path}.headerRows", "headerRows must be present when header is true.");
        if (!dialect.Header && dialect.HeaderRows is { Count: > 0 })
            Add(issues, $"{path}.headerRows", "headerRows must be absent when header is false.");
    }

    private static void ValidateDialectRows(TableDialect dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect.HeaderRows?.Any(row => row < 1) == true || dialect.CommentRows?.Any(row => row < 1) == true)
            Add(issues, path, "Header and comment row numbers must be positive.");
    }

    private static void ValidateDialectCharacters(TableDialect dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect.EscapeChar is not null && dialect.QuoteChar is not null)
            Add(issues, path, "escapeChar and quoteChar are mutually exclusive.");
    }

    private static void ValidateSpreadsheetDialect(TableDialect dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect.SheetName is not null && dialect.SheetNumber is not null)
            Add(issues, path, "sheetName and sheetNumber are mutually exclusive.");
        if (dialect.SheetNumber < 1)
            Add(issues, $"{path}.sheetNumber", "sheetNumber must be positive.");
    }

    private static void ValidateStructuredDialect(TableDialect dialect, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (dialect.ItemType is not null && dialect.ItemType is not ("array" or "object"))
            Add(issues, $"{path}.itemType", "itemType must be 'array' or 'object'.");
    }

    private static void ValidateContributors(IEnumerable<Contributor> contributors, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var validator = new ContributorValidator();
        var index = 0;
        foreach (var contributor in contributors)
        {
            if (!validator.IsValid(contributor)) Add(issues, $"{path}[{index}]", "Contributor is invalid.");
            index++;
        }
    }

    private static void ValidateLicenses(IEnumerable<License> licenses, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var validator = new LicenseValidator();
        var index = 0;
        foreach (var license in licenses)
        {
            if (!validator.IsValid(license)) Add(issues, $"{path}[{index}]", "License is invalid.");
            index++;
        }
    }

    private static void ValidateSources(IEnumerable<Source> sources, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        var index = 0;
        foreach (var source in sources)
        {
            ValidateSource(source, $"{path}[{index}]", issues);
            index++;
        }
    }

    private static void ValidateSource(Source source, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (IsEmpty(source))
            Add(issues, path, "A source must define at least one property.");
        ValidateSourceEmail(source.Email, path, issues);
        ValidateSourcePath(source.Path, path, issues);
    }

    private static bool IsEmpty(Source source)
        => source.Title is null && source.Path is null && source.Email is null && source.Version is null;

    private static void ValidateSourceEmail(string? email, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (email is not null &&
            !Regex.IsMatch(email, DefaultRegex.EmailRegex, RegexOptions.None, RegexTimeout))
            Add(issues, $"{path}.email", "Source email is invalid.");
    }

    private static void ValidateSourcePath(string? sourcePath, string path,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (sourcePath is not null &&
            !Regex.IsMatch(sourcePath, DefaultRegex.PathRegex, RegexOptions.None, RegexTimeout))
            Add(issues, $"{path}.path", "Source path is invalid.");
    }

    private static void Add(ICollection<DataPackageValidationIssue> issues, string path, string message)
        => issues.Add(new(path, message));
}
