using Packata.AdapterMapping;
using Packata.OpenDataContract.ServerTypes;

namespace Packata.OpenDataContract.Mapping;

internal enum BindingOwner
{
    Other,
    Schema,
    Field,
    Server,
}

internal sealed class OdcsBindings
{
    internal BindingSelector? AllowAdditionalProperties { get; }
    internal BindingSelector? EnforcePropertyOrder { get; }
    internal BindingSelector? CategoryFieldMap { get; }
    internal BindingSelector? Categories { get; }
    internal BindingSelector? DefaultValue { get; }
    internal BindingSelector? DefaultExpression { get; }
    internal BindingSelector? Delimiter { get; }
    internal BindingSelector? QuoteChar { get; }
    internal BindingSelector? LineTerminator { get; }
    internal BindingSelector? Header { get; }
    internal BindingSelector? SheetName { get; }
    internal BindingSelector? SheetNumber { get; }
    internal BindingSelector? HeaderRows { get; }
    internal BindingSelector? CommentRows { get; }
    private readonly (BindingSelector? Selector, BindingOwner Owner)[] entries;

    internal static OdcsBindings Default { get; } = new(OdcsMappingOptions.Default);

    internal OdcsBindings(OdcsMappingOptions options)
    {
        AllowAdditionalProperties = Resolve(BindingOwner.Schema, "allowAdditionalProperties", options.Schema.AllowAdditionalProperties);
        EnforcePropertyOrder = Resolve(BindingOwner.Schema, "enforcePropertyOrder", options.Schema.EnforcePropertyOrder);
        CategoryFieldMap = Resolve(BindingOwner.Schema, "categoryFieldMap", options.Schema.CategoryFieldMap);
        Categories = Resolve(BindingOwner.Field, "categories", options.Field.Categories);
        DefaultValue = Resolve(BindingOwner.Field, "defaultValue", options.Field.DefaultValue);
        DefaultExpression = Resolve(BindingOwner.Field, "defaultExpression", options.Field.DefaultExpression);
        Delimiter = Resolve(BindingOwner.Server, "delimiter", options.Server.Delimiter);
        QuoteChar = Resolve(BindingOwner.Server, "quoteChar", options.Server.QuoteChar);
        LineTerminator = Resolve(BindingOwner.Server, "lineTerminator", options.Server.LineTerminator);
        Header = Resolve(BindingOwner.Server, "header", options.Server.Header);
        SheetName = Resolve(BindingOwner.Server, "sheetName", options.Server.SheetName);
        SheetNumber = Resolve(BindingOwner.Server, "sheetNumber", options.Server.SheetNumber);
        HeaderRows = Resolve(BindingOwner.Server, "headerRows", options.Server.HeaderRows);
        CommentRows = Resolve(BindingOwner.Server, "commentRows", options.Server.CommentRows);
        entries =
        [
            (AllowAdditionalProperties, BindingOwner.Schema),
            (EnforcePropertyOrder, BindingOwner.Schema),
            (CategoryFieldMap, BindingOwner.Schema),
            (Categories, BindingOwner.Field),
            (DefaultValue, BindingOwner.Field),
            (DefaultExpression, BindingOwner.Field),
            (Delimiter, BindingOwner.Server),
            (QuoteChar, BindingOwner.Server),
            (LineTerminator, BindingOwner.Server),
            (Header, BindingOwner.Server),
            (SheetName, BindingOwner.Server),
            (SheetNumber, BindingOwner.Server),
            (HeaderRows, BindingOwner.Server),
            (CommentRows, BindingOwner.Server),
        ];
        foreach (var group in entries.GroupBy(entry => entry.Owner))
            BindingSelector.CheckCollisions(group.Select(entry => entry.Selector));
        if (entries.Select(entry => entry.Selector).OfType<BindingSelector>()
            .Any(selector => selector.Matches("propertyMatching", "packata") || selector.Matches("propertyOrder", "packata")))
            throw new ArgumentException("The obsolete Packata propertyMatching and propertyOrder selectors are reserved.");
    }

    private static BindingSelector? Resolve(BindingOwner owner, string property, OdcsBindingOverride? replacement)
    {
        if (replacement is { IsDisabled: true }) return null;
        return BindingSelector.Resolve($"{owner.ToString().ToLowerInvariant()}.{property}", property,
            replacement?.Property, replacement is { InheritVendor: false } ? replacement.Vendor : "packata",
            owner == BindingOwner.Server ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal IEnumerable<BindingSelector> ForOwner(BindingOwner owner)
        => entries.Where(entry => entry.Owner == owner).Select(entry => entry.Selector).OfType<BindingSelector>();

    internal bool HasTypedNativeValue(CustomProperty property)
        => ForOwner(BindingOwner.Schema).Concat(ForOwner(BindingOwner.Field))
            .Any(selector => selector.Matches(property.Property, property.Vendor));

    internal static object? ReadValue(BindingSelector selector, CustomProperty property)
        => selector.Matches(selector.BuiltInProperty, "packata") ? property.Value : property.ReadMappingValue();

    // Structural bindings already enforce placement. Apply the same check to custom replacements.
    private bool ChecksPlacement(BindingSelector selector)
        => selector == AllowAdditionalProperties || selector == EnforcePropertyOrder
            || !selector.Matches(selector.BuiltInProperty, "packata");

    internal BindingSelector? Misplaced(object source, CustomProperty property)
    {
        var owner = source switch
        {
            SchemaObject => BindingOwner.Schema,
            SchemaProperty => BindingOwner.Field,
            BaseServer => BindingOwner.Server,
            _ => BindingOwner.Other,
        };
        if (ForOwner(owner).Any(selector => selector.Matches(property.Property, property.Vendor))) return null;
        return entries.Select(entry => entry.Selector).OfType<BindingSelector>()
            .FirstOrDefault(selector => ChecksPlacement(selector) && selector.Matches(property.Property, property.Vendor));
    }
}
