namespace Packata.OpenDataContract.Mapping;

/// <summary>Overrides one ODCS selector while keeping its source owner and canonical feature.</summary>
public sealed record OdcsBindingOverride
{
    private OdcsBindingOverride(string? property, string? vendor, bool inheritVendor)
    {
        Property = property;
        Vendor = vendor;
        InheritVendor = inheritVendor;
    }

    public string? Property { get; }
    public string? Vendor { get; }
    public bool InheritVendor { get; }

    /// <summary>Uses the built-in vendor. Omit the property name to keep its built-in name.</summary>
    public static OdcsBindingOverride UseBuiltInVendor(string? property = null) => new(property, null, true);

    /// <summary>Matches a declaration with no vendor. Omit the property name to keep its built-in name.</summary>
    public static OdcsBindingOverride Unvendored(string? property = null) => new(property, null, false);

    /// <summary>Matches the specified vendor. Omit the property name to keep its built-in name.</summary>
    public static OdcsBindingOverride ForVendor(string vendor, string? property = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        return new(property, vendor, false);
    }
}
