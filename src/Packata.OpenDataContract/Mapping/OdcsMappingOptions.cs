namespace Packata.OpenDataContract.Mapping;

/// <summary>Overrides ODCS selectors by source owner. Unset bindings use the built-in selectors.</summary>
public sealed record OdcsMappingOptions
{
    public static OdcsMappingOptions Default { get; } = new();

    public static OdcsMappingOptions NoCustomPropertyBindings { get; } = new()
    {
        Schema = new()
        {
            AllowAdditionalProperties = OdcsBindingOverride.Disabled,
            EnforcePropertyOrder = OdcsBindingOverride.Disabled,
            CategoryFieldMap = OdcsBindingOverride.Disabled,
        },
        Field = new()
        {
            Categories = OdcsBindingOverride.Disabled,
            DefaultValue = OdcsBindingOverride.Disabled,
            DefaultExpression = OdcsBindingOverride.Disabled,
        },
        Server = new()
        {
            Delimiter = OdcsBindingOverride.Disabled,
            QuoteChar = OdcsBindingOverride.Disabled,
            LineTerminator = OdcsBindingOverride.Disabled,
            Header = OdcsBindingOverride.Disabled,
            SheetName = OdcsBindingOverride.Disabled,
            SheetNumber = OdcsBindingOverride.Disabled,
            HeaderRows = OdcsBindingOverride.Disabled,
            CommentRows = OdcsBindingOverride.Disabled,
        },
    };

    public OdcsSchemaBindings Schema { get; init; } = new();

    public OdcsFieldBindings Field { get; init; } = new();

    public OdcsServerBindings Server { get; init; } = new();
}

public sealed record OdcsSchemaBindings
{
    public OdcsBindingOverride AllowAdditionalProperties { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride EnforcePropertyOrder { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride CategoryFieldMap { get; init; } = OdcsBindingOverride.BuiltIn;
}

public sealed record OdcsFieldBindings
{
    public OdcsBindingOverride Categories { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride DefaultValue { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride DefaultExpression { get; init; } = OdcsBindingOverride.BuiltIn;
}

public sealed record OdcsServerBindings
{
    public OdcsBindingOverride Delimiter { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride QuoteChar { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride LineTerminator { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride Header { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride SheetName { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride SheetNumber { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride HeaderRows { get; init; } = OdcsBindingOverride.BuiltIn;

    public OdcsBindingOverride CommentRows { get; init; } = OdcsBindingOverride.BuiltIn;
}
