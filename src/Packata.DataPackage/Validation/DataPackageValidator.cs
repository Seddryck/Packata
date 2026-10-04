using System.Text.RegularExpressions;

namespace Packata.DataPackage.Validation;

/// <summary>Validates a complete Data Package v2 descriptor and reports JSON-path diagnostics.</summary>
public sealed class DataPackageValidator
{
    private static readonly HashSet<string> FieldTypes =
    [
        "string", "number", "integer", "date", "time", "datetime", "year", "yearmonth",
        "boolean", "object", "geopoint", "geojson", "array", "duration", "any"
    ];

    public DataPackageValidationResult Validate(DataPackage package)
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

    public bool IsValid(DataPackage package) => Validate(package).IsValid;

    private static void ValidateResource(Resource resource, string path, DataPackage package,
        ICollection<DataPackageValidationIssue> issues)
    {
        var hasPaths = resource.Paths.Count > 0;
        var hasData = resource.Data is not null;
        if (hasPaths && hasData)
            Add(issues, path, "A resource cannot define both path and data.");
        if (string.Equals(resource.Type, "table", StringComparison.OrdinalIgnoreCase) && !hasPaths && !hasData)
            Add(issues, path, "A tabular resource must define path or data.");
        if (resource.Type is not null && !resource.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
            Add(issues, $"{path}.type", "The only standard Data Package v2 resource type is 'table'.");

        if (hasPaths)
        {
            var fullyQualified = resource.Paths.Select(item => item.IsFullyQualified).Distinct().ToArray();
            if (fullyQualified.Length > 1)
                Add(issues, $"{path}.path", "A path array cannot mix relative paths and fully qualified URLs.");
        }
        if (resource.Bytes < 0)
            Add(issues, $"{path}.bytes", "Resource bytes cannot be negative.");

        ValidateSources(resource.Sources, $"{path}.sources", issues);
        ValidateLicenses(resource.Licenses, $"{path}.licenses", issues);
        ValidateSchema(resource.Schema, $"{path}.schema", resource, package, issues);
        ValidateDialect(resource.Dialect, $"{path}.dialect", issues);
    }

    private static void ValidateSchema(Schema? schema, string path, Resource resource, DataPackage package,
        ICollection<DataPackageValidationIssue> issues)
    {
        if (schema is null) return;
        if (schema.Fields.Count == 0)
            Add(issues, $"{path}.fields", "A table schema must contain at least one field.");

        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < schema.Fields.Count; index++)
        {
            var field = schema.Fields[index];
            var fieldPath = $"{path}.fields[{index}]";
            if (string.IsNullOrWhiteSpace(field.Name))
                Add(issues, $"{fieldPath}.name", "A field name is required.");
            else if (!fields.Add(field.Name))
                Add(issues, $"{fieldPath}.name", $"Field name '{field.Name}' must be unique within the schema.");
            if (field.Type is not null && !FieldTypes.Contains(field.Type))
                Add(issues, $"{fieldPath}.type", $"Field type '{field.Type}' is not defined by Data Package v2.");
            if (field.CategoriesOrdered is not null && field.Categories is null)
                Add(issues, $"{fieldPath}.categoriesOrdered", "categoriesOrdered requires categories.");
        }

        ValidateKey(schema.PrimaryKey, $"{path}.primaryKey", fields, issues);
        for (var index = 0; index < (schema.UniqueKeys?.Count ?? 0); index++)
            ValidateKey(schema.UniqueKeys![index], $"{path}.uniqueKeys[{index}]", fields, issues);

        for (var index = 0; index < (schema.ForeignKeys?.Count ?? 0); index++)
        {
            var foreignKey = schema.ForeignKeys![index];
            var foreignPath = $"{path}.foreignKeys[{index}]";
            ValidateKey(foreignKey.Fields, $"{foreignPath}.fields", fields, issues);
            if (foreignKey.Reference is null)
            {
                Add(issues, $"{foreignPath}.reference", "A foreign key reference is required.");
                continue;
            }
            if (foreignKey.Fields.Count != foreignKey.Reference.Fields.Count)
                Add(issues, foreignPath, "Foreign-key fields and reference fields must have the same length.");
            var target = string.IsNullOrWhiteSpace(foreignKey.Reference.Resource)
                ? resource
                : package.Resources.FirstOrDefault(item => item.Name == foreignKey.Reference.Resource);
            if (target is null)
            {
                Add(issues, $"{foreignPath}.reference.resource",
                    $"Referenced resource '{foreignKey.Reference.Resource}' does not exist.");
                continue;
            }
            var targetFields = target.Schema?.Fields.Where(item => item.Name is not null)
                .Select(item => item.Name!).ToHashSet(StringComparer.Ordinal) ?? [];
            ValidateKey(foreignKey.Reference.Fields, $"{foreignPath}.reference.fields", targetFields, issues);
        }
    }

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
        if (dialect.Header && (dialect.HeaderRows is null || dialect.HeaderRows.Count == 0))
            Add(issues, $"{path}.headerRows", "headerRows must be present when header is true.");
        if (!dialect.Header && dialect.HeaderRows is { Count: > 0 })
            Add(issues, $"{path}.headerRows", "headerRows must be absent when header is false.");
        if (dialect.HeaderRows?.Any(row => row < 1) == true || dialect.CommentRows?.Any(row => row < 1) == true)
            Add(issues, path, "Header and comment row numbers must be positive.");
        if (dialect.EscapeChar is not null && dialect.QuoteChar is not null)
            Add(issues, path, "escapeChar and quoteChar are mutually exclusive.");
        if (dialect.SheetName is not null && dialect.SheetNumber is not null)
            Add(issues, path, "sheetName and sheetNumber are mutually exclusive.");
        if (dialect.SheetNumber < 1)
            Add(issues, $"{path}.sheetNumber", "sheetNumber must be positive.");
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
            var sourcePath = $"{path}[{index}]";
            if (source.Title is null && source.Path is null && source.Email is null && source.Version is null)
                Add(issues, sourcePath, "A source must define at least one property.");
            if (source.Email is not null && !Regex.IsMatch(source.Email, DefaultRegex.EmailRegex))
                Add(issues, $"{sourcePath}.email", "Source email is invalid.");
            if (source.Path is not null && !Regex.IsMatch(source.Path, DefaultRegex.PathRegex))
                Add(issues, $"{sourcePath}.path", "Source path is invalid.");
            index++;
        }
    }

    private static void Add(ICollection<DataPackageValidationIssue> issues, string path, string message)
        => issues.Add(new(path, message));
}
