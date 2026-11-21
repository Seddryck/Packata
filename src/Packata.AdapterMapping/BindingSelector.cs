namespace Packata.AdapterMapping;

/// <summary>Resolves source selectors and checks collisions within one source owner.</summary>
internal sealed record BindingSelector(string Id, string BuiltInProperty, string Property, string? Vendor, StringComparison Comparison)
{
    internal static BindingSelector Resolve(string id, string builtInProperty, string? replacement, string? vendor = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        if (replacement is not null && string.IsNullOrWhiteSpace(replacement))
            throw new ArgumentException($"Binding '{id}' requires a non-empty property name.");
        if (vendor is not null && string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException($"Binding '{id}' requires a non-empty vendor name or null.");
        return new(id, builtInProperty, replacement ?? builtInProperty, vendor, comparison);
    }

    internal bool Matches(string? property, string? vendor = null)
        => string.Equals(Property, property, Comparison) && string.Equals(Vendor, vendor, Comparison);

    internal static void CheckCollisions(IEnumerable<BindingSelector> selectors)
    {
        var seen = new List<BindingSelector>();
        foreach (var selector in selectors)
        {
            var conflict = seen.Find(previous => previous.Matches(selector.Property, selector.Vendor)
                || selector.Matches(previous.Property, previous.Vendor));
            if (conflict is not null)
                throw new ArgumentException($"Bindings '{conflict.Id}' and '{selector.Id}' select the same custom property '{selector.Property}'.");
            seen.Add(selector);
        }
    }
}
