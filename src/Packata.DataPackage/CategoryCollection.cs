using System.Collections;

namespace Packata.DataPackage;

/// <summary>Either standard inline categories or a Category Tables recipe reference.</summary>
public sealed class CategoryCollection : IReadOnlyList<ICategory>
{
    private readonly List<ICategory> _values = [];
    public CategoryTableReference? Reference { get; }
    public CategoryCollection() { }
    public CategoryCollection(IEnumerable<ICategory> values) => _values.AddRange(values);
    public CategoryCollection(CategoryTableReference reference) => Reference = reference ?? throw new ArgumentNullException(nameof(reference));
    public void Add(ICategory category)
    {
        if (Reference is not null) throw new InvalidOperationException("A category reference cannot also contain inline values.");
        _values.Add(category);
    }
    public int Count => _values.Count;
    public ICategory this[int index] => _values[index];
    public IEnumerator<ICategory> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public static implicit operator CategoryCollection?(List<ICategory>? values) => values is null ? null : new(values);
    public static implicit operator CategoryCollection(CategoryTableReference reference) => new(reference);
}

/// <summary>A resource reference from the Category Tables recipe, not the DPv2.0 inline syntax.</summary>
public sealed class CategoryTableReference
{
    public string? Resource { get; set; }
    public string? Package { get; set; }
    /// <summary>Omission means value; the absent state is preserved when writing.</summary>
    public string? EncodedAs { get; set; }
}

/// <summary>Identifies the value and optional label fields of a category table resource.</summary>
public sealed class CategoryFieldMap
{
    public string? Value { get; set; }
    public string? Label { get; set; }
    /// <summary>Null makes no assumption about ordering, unlike explicit false.</summary>
    public bool? Ordered { get; set; }
}
