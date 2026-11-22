namespace Packata.DataPackage.Mapping;

/// <summary>Configures the bindings in field-level packata:customProperties lists.</summary>
public sealed record DataPackageMappingOptions
{
    public static DataPackageMappingOptions Default { get; } = new();

    public static DataPackageMappingOptions NoCustomPropertyBindings { get; } = new()
    {
        DefaultValue = DataPackageBindingOverride.Disabled,
        DefaultExpression = DataPackageBindingOverride.Disabled,
        PhysicalTypeContext = DataPackageBindingOverride.Disabled,
    };

    public DataPackageBindingOverride DefaultValue { get; init; } = DataPackageBindingOverride.BuiltIn;

    public DataPackageBindingOverride DefaultExpression { get; init; } = DataPackageBindingOverride.BuiltIn;

    public DataPackageBindingOverride PhysicalTypeContext { get; init; } = DataPackageBindingOverride.BuiltIn;
}
