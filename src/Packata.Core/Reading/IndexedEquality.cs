using Packata.Core.Contracts;

namespace Packata.Core.Reading;

/// <summary>Requires the declared index for a typed equality predicate on a schema property.</summary>
public sealed record IndexedEquality(string Property, DataIndexKind Kind, object Value);

/// <summary>Applies all specified equality predicates. The optional path predicate prunes partitions first.</summary>
public sealed record IndexedRead(IReadOnlyList<IndexedEquality> Equalities, Func<string, bool>? PartitionFilter = null);
