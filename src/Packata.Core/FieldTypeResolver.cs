using Packata.Core.Contracts;

namespace Packata.Core;

/// <summary>Interprets canonical fields without guessing dialect-specific physical types.</summary>
public static class FieldTypeResolver
{
    public static FieldTypeResolution Resolve(DataField field, TypeMappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(options);
        return new FieldTypeResolutionContext(field, options, options.SourcePath ?? field.Name).Resolve();
    }
}
