namespace Packata.DataPackage.Mapping;

/// <summary>Selects a property in the field's packata:customProperties list or disables the binding.</summary>
public sealed record DataPackageBindingOverride
{
    private DataPackageBindingOverride(string? property, bool isDisabled)
    {
        Property = property;
        IsDisabled = isDisabled;
    }

    public static DataPackageBindingOverride BuiltIn { get; } = new(null, false);

    public static DataPackageBindingOverride Disabled { get; } = new(null, true);

    public string? Property { get; }

    public bool IsDisabled { get; }

    public static DataPackageBindingOverride FromProperty(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return new(property, false);
    }
}
