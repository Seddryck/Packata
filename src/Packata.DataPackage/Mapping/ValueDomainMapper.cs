using System.Text.RegularExpressions;
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
        var type = field.Type ?? (field is StringField ? "string" : field is IntegerField ? "integer" : "any");
        try
        {
            IReadOnlyList<ScalarValue>? allowed = enumeration is null ? null : Scalars(enumeration.Value, type, allowNull: true);
            if (field.Categories?.Reference is { } reference)
            {
                if (type is not ("string" or "integer")) throw new ArgumentException("Category table references require a string or integer field.");
                ValueEncoding? encoding = reference.EncodedAs switch
                { null => null, "value" => ValueEncoding.Value, "label" => ValueEncoding.Label, _ => throw new ArgumentException("encodedAs must be value or label.") };
                PackageLocator? locator = null;
                if (reference.Package is { } name)
                {
                    if (Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        locator = new(PackageLocatorKind.DescriptorUri, name);
                    else if (Regex.IsMatch(name, @"^[a-z0-9][a-z0-9._-]*$")) locator = new(PackageLocatorKind.RegistryName, name);
                    else throw new ArgumentException("Expected an HTTP(S) package descriptor URL or registry package name.");
                }
                string? bound = null;
                if (locator is null)
                {
                    var target = package.Resources.FirstOrDefault(resource => resource.Name == reference.Resource);
                    if (target is not null && tables.GetValueOrDefault(target) is { } table)
                    {
                        var valueField = target.Schema!.Fields.First(item => item.Name == table.ValueField.Segments[0]);
                        if (valueField.Type != type || encoding == ValueEncoding.Label && table.LabelField is null)
                            Warn(diagnostics, path + ".categories", "The referenced table's value type or label mapping is incompatible with this field.");
                        else bound = target.Name;
                    }
                    else Warn(diagnostics, path + ".categories.resource", "The local category table could not be bound; its reference was retained without known membership values.");
                }
                return new ValueDomain(allowedValues: allowed, ordered: field.CategoriesOrdered,
                    reference: new ValueDomainReference(reference.Resource!, locator, encoding, bound));
            }
            var entries = new List<ValueOption>();
            if (field.Categories is not null)
            {
                if (type is not ("string" or "integer")) throw new ArgumentException("Inline categories require a string or integer field.");
                var values = Scalars(field.Categories.Select(entry => (object?)entry.Value), type, allowNull: false);
                for (var index = 0; index < values.Count; index++)
                    entries.Add(new ValueOption(values[index], field.Categories[index] is Category ? field.Categories[index].Label : null));
            }
            else entries.AddRange(allowed!.Select(value => new ValueOption(value)));
            return new ValueDomain(entries, allowed ?? entries.Select(entry => entry.Value).ToArray(), field.Categories is null ? null : field.CategoriesOrdered);
        }
        catch (ArgumentException exception)
        {
            Warn(diagnostics, path + (field.Categories is null ? ".constraints.enum" : ".categories"), exception.Message + " Original constraints and source extensions were retained without a scalar domain.");
            return null;
        }
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
