using Packata.Core.Contracts;

namespace Packata.Core;

public class RuntimeTypeMapper : BaseTypeMapper<Type>
{
    protected override Type DefaultMapping => typeof(object);

    protected override bool TryMapFormat(string type, string? format, out Type mapping)
    {
        mapping = typeof(DateTimeOffset);
        return TypeMappingCatalog.HasOffset(type, format);
    }

    protected override void Initialize()
    {
        foreach (var entry in TypeMappingCatalog.Entries.Where(TypeMappingCatalog.IsLegacyEntry))
            RegisterDefault(entry.LogicalType, entry.Format, entry.RuntimeType);
    }

    public FieldTypeResolution Resolve(DataField field, TypeMappingOptions options)
    {
        return ApplyRegistrations(FieldTypeResolver.Resolve(field, options), options);
    }

    private FieldTypeResolution ApplyRegistrations(FieldTypeResolution result, TypeMappingOptions options)
    {
        var items = result.Items is null ? null : ApplyRegistrations(result.Items, options);
        var children = result.Children.Select(child => ApplyRegistrations(child, options)).ToArray();
        result = result.WithNested(items, children);
        if (options.PreserveLegacyRepresentation)
            return result.RuntimeType == Map(result.Field.LogicalType, result.SourceFormat) ? result
                : result.WithRuntime(Map(result.Field.LogicalType, result.SourceFormat), "LegacyRepresentation");
        if (result.LogicalType == "array" && items?.RuntimeType is { } itemType
            && !result.Diagnostics.Any(d => d.FieldPath == result.FieldPath
                && d.Target == TypeMappingTarget.Source && d.Severity == TypeMappingSeverity.Error))
            result = result with { RuntimeType = itemType.MakeArrayType() };
        if (TryGetResolutionMapping(result, typeof(DateTimeOffset), out var custom))
            return result.WithRuntime(custom, "CustomRegistration");
        return result;
    }
}
