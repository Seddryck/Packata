using Packata.Core.Contracts;
using Packata.DataPackage.Validation;

namespace Packata.DataPackage.Mapping;

internal static class ValueDomainMapper
{
    public static CategoryTableDefinition? Table(Resource resource, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (resource.CategoryFieldMap is not { } map) return null;
        var issues = new List<DataPackageValidationIssue>();
        CategoryTableValidator.ValidateResource(resource, path, issues);
        if (issues.Count > 0)
        {
            foreach (var issue in issues) Warn(diagnostics, issue.Path, issue.Message);
            return null;
        }
        return new CategoryTableDefinition(new FieldPath(map.Value!), map.Label is null ? null : new FieldPath(map.Label), map.Ordered,
            ExtensionMetadata.For("datapackage", new Dictionary<string, object?> { ["value"] = map.Value, ["label"] = map.Label, ["ordered"] = map.Ordered }));
    }

    public static ValueDomain? Field(Field field, DataPackage package, IReadOnlyDictionary<Resource, CategoryTableDefinition?> tables,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var enumeration = field.Constraints?.Get<EnumConstraint>();
        if (field.Categories is null && enumeration is null) return null;
        var type = FieldTypeNames.Get(field);
        try
        {
            IReadOnlyList<ScalarValue>? allowed = enumeration is null ? null : Scalars(enumeration.Value, type, allowNull: true);
            return field.Categories?.Reference is { } reference
                ? ReferencedDomain(field, reference, allowed, package, tables, path, diagnostics)
                : InlineDomain(field, type, allowed);
        }
        catch (ArgumentException exception)
        {
            Warn(diagnostics, path + (field.Categories is null ? ".constraints.enum" : ".categories"),
                exception.Message + " Original constraints and source extensions were retained without a scalar domain.");
            return null;
        }
    }

    private static ValueDomain ReferencedDomain(Field field, CategoryTableReference reference,
        IReadOnlyList<ScalarValue>? allowed, DataPackage package, IReadOnlyDictionary<Resource, CategoryTableDefinition?> tables,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var type = FieldTypeNames.Get(field);
        RequireCategoryType(type, "Category table references require a string or integer field.");
        ValueEncoding? encoding = reference.EncodedAs switch
        {
            null => null,
            "value" => ValueEncoding.Value,
            "label" => ValueEncoding.Label,
            _ => throw new ArgumentException("encodedAs must be value or label.")
        };
        var locator = PackageLocator(reference.Package);
        var bound = locator is null ? BindLocalReference(reference, type, encoding, package, tables, path, diagnostics) : null;
        return new ValueDomain(allowedValues: allowed, ordered: field.CategoriesOrdered,
            reference: new ValueDomainReference(reference.Resource!, locator, encoding, bound));
    }

    private static PackageLocator? PackageLocator(string? name)
    {
        if (name is null) return null;
        if (PackageReferenceSyntax.IsDescriptorUri(name)) return new(PackageLocatorKind.DescriptorUri, name);
        if (PackageReferenceSyntax.IsRegistryName(name)) return new(PackageLocatorKind.RegistryName, name);
        throw new ArgumentException("Expected an HTTP(S) package descriptor URL or registry package name.");
    }

    private static string? BindLocalReference(CategoryTableReference reference, string type, ValueEncoding? encoding,
        DataPackage package, IReadOnlyDictionary<Resource, CategoryTableDefinition?> tables,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var target = package.Resources.FirstOrDefault(resource => resource.Name == reference.Resource);
        if (target is null || tables.GetValueOrDefault(target) is not { } table)
        {
            Warn(diagnostics, path + ".categories.resource", "The local category table could not be bound; its reference was retained without known membership values.");
            return null;
        }
        var valueField = target.Schema!.Fields.First(item => item.Name == table.ValueField.Segments[0]);
        if (FieldTypeNames.Get(valueField) != type || encoding == ValueEncoding.Label && table.LabelField is null)
        {
            Warn(diagnostics, path + ".categories", "The referenced table's value type or label mapping is incompatible with this field.");
            return null;
        }
        return target.Name;
    }

    private static ValueDomain InlineDomain(Field field, string type, IReadOnlyList<ScalarValue>? allowed)
    {
        var entries = field.Categories is { } categories
            ? InlineOptions(categories, type)
            : allowed!.Select(value => new ValueOption(value)).ToArray();
        return new ValueDomain(entries, allowed ?? entries.Select(entry => entry.Value).ToArray(),
            field.Categories is null ? null : field.CategoriesOrdered);
    }

    private static IReadOnlyList<ValueOption> InlineOptions(CategoryCollection categories, string type)
    {
        RequireCategoryType(type, "Inline categories require a string or integer field.");
        var values = Scalars(categories.Select(entry => (object?)entry.Value), type, allowNull: false);
        return values.Select((value, index) => new ValueOption(value, categories[index] is Category ? categories[index].Label : null)).ToArray();
    }

    private static void RequireCategoryType(string type, string message)
    {
        if (!FieldTypeNames.SupportsCategories(type)) throw new ArgumentException(message);
    }

    private static IReadOnlyList<ScalarValue> Scalars(IEnumerable<object?> values, string type, bool allowNull)
    {
        var result = new List<ScalarValue>();
        foreach (var value in values)
        {
            if (!ScalarValue.TryCreate(value, out var scalar) || scalar.Kind == ScalarKind.Null && !allowNull || scalar.Kind != ScalarKind.Null && !scalar.IsCompatibleWith(type))
                throw new ArgumentException($"Value at index {result.Count} is non-scalar, outside the supported scalar range, or incompatible with the field type.");
            result.Add(scalar);
        }
        return result;
    }
    private static void Warn(ICollection<MappingDiagnostic> diagnostics, string path, string message)
        => diagnostics.Add(new("DP006", MappingSeverity.Warning, path, message));
}
