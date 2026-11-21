namespace Packata.DataPackage.Mapping;

/// <summary>Overrides property names in field-level packata:customProperties lists.</summary>
public sealed record DataPackageMappingOptions
{
    public string? DefaultValueProperty { get; init; }

    public string? DefaultExpressionProperty { get; init; }
}
