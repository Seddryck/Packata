using System.Data;
using Packata.Core.Contracts;

namespace Packata.Core;

/// <summary>Compatibility adapter over shared interpretation; dialect rendering belongs to provisioning.</summary>
public class DbTypeMapper : BaseTypeMapper<DbType>
{
    protected override DbType DefaultMapping => DbType.Object;

    protected override bool TryMapFormat(string type, string? format, out DbType mapping)
    {
        mapping = DbType.DateTimeOffset;
        return TypeMappingCatalog.HasOffset(type, format);
    }

    protected override void Initialize()
    {
        foreach (var entry in TypeMappingCatalog.Entries.Where(TypeMappingCatalog.IsLegacyEntry))
            RegisterDefault(entry.LogicalType, entry.Format, entry.DatabaseType);
    }

    public FieldTypeResolution Resolve(DataField field, TypeMappingOptions options)
        => ApplyRegistrations(FieldTypeResolver.Resolve(field, options), options);

    private FieldTypeResolution ApplyRegistrations(FieldTypeResolution result, TypeMappingOptions options)
    {
        var field = result.Field;
        var items = result.Items is null ? null : ApplyRegistrations(result.Items, options);
        var children = result.Children.Select(child => ApplyRegistrations(child, options)).ToArray();
        result = result.WithNested(items, children);
        if (options.PreserveLegacyRepresentation)
            return result.DatabaseType == Map(field.LogicalType, field.Format) ? result
                : result.WithDatabase(Map(field.LogicalType, field.Format), "LegacyRepresentation");
        if (TryGetResolutionMapping(result, DbType.DateTimeOffset, out var custom))
            return result.WithDatabase(custom, "CustomRegistration");
        return result;
    }
}
