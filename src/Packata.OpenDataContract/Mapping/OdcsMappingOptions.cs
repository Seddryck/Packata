namespace Packata.OpenDataContract.Mapping;

/// <summary>Overrides ODCS selectors by source owner. Unset bindings use the built-in selectors.</summary>
public sealed record OdcsMappingOptions
{
    public OdcsSchemaBindings Schema { get; init; } = new();
    public OdcsFieldBindings Field { get; init; } = new();
    public OdcsServerBindings Server { get; init; } = new();
}

public sealed record OdcsSchemaBindings
{
    public OdcsBindingOverride? AllowAdditionalProperties { get; init; }
    public OdcsBindingOverride? EnforcePropertyOrder { get; init; }
    public OdcsBindingOverride? CategoryFieldMap { get; init; }
}

public sealed record OdcsFieldBindings
{
    public OdcsBindingOverride? Categories { get; init; }
    public OdcsBindingOverride? DefaultValue { get; init; }
    public OdcsBindingOverride? DefaultExpression { get; init; }
}

public sealed record OdcsServerBindings
{
    public OdcsBindingOverride? Delimiter { get; init; }
    public OdcsBindingOverride? QuoteChar { get; init; }
    public OdcsBindingOverride? LineTerminator { get; init; }
    public OdcsBindingOverride? Header { get; init; }
    public OdcsBindingOverride? SheetName { get; init; }
    public OdcsBindingOverride? SheetNumber { get; init; }
    public OdcsBindingOverride? HeaderRows { get; init; }
    public OdcsBindingOverride? CommentRows { get; init; }
}
