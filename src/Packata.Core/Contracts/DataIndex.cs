namespace Packata.Core.Contracts;

/// <summary>A storage-independent index on one top-level schema field.</summary>
public sealed record DataIndex(
    string Property,
    DataIndexKind Kind,
    DataIndexOrder? Order = null,
    DataIndexComparison? Comparison = null);

public enum DataIndexKind
{
    Ordered,
    Bloom
}

public enum DataIndexOrder
{
    Ascending,
    Descending
}

public enum DataIndexComparison
{
    Ordinal,
    OrdinalIgnoreCase
}
