namespace Packata.Core.Contracts;

/// <summary>Shared detection of changes to declared property-set and sequence guarantees.</summary>
internal static class SchemaPropertyComparison
{
    internal static bool ClosedSetChanged(DataSchema? previous, DataSchema? current)
    {
        if (!IsClosed(previous) && !IsClosed(current)) return false;
        return !Names(previous).ToHashSet(StringComparer.Ordinal).SetEquals(Names(current));
    }

    internal static bool OrderChanged(DataSchema? previous, DataSchema? current)
    {
        if (previous?.PropertyOrder != PropertyOrder.Declared && current?.PropertyOrder != PropertyOrder.Declared) return false;
        return !Names(previous).SequenceEqual(Names(current), StringComparer.Ordinal);
    }

    private static bool IsClosed(DataSchema? schema) => schema?.PropertyMatching is PropertyMatching.Equal or PropertyMatching.Superset;
    private static IEnumerable<string> Names(DataSchema? schema) => schema?.Fields.Select(field => field.Name) ?? [];
}
