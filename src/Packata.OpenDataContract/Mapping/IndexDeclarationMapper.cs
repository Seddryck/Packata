using System.Collections;
using System.Text.Json;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static class IndexDeclarationMapper
{
    internal static bool IsDeclaration(CustomProperty property) =>
        property is { Vendor: "packata", Property: "indexes" };

    public static IReadOnlyList<DataIndex> Map(SchemaObject schema, ICollection<MappingDiagnostic> diagnostics)
    {
        var declarations = schema.CustomProperties.Select((entry, index) => (entry, index))
            .Where(item => IsDeclaration(item.entry)).ToArray();
        if (declarations.Length == 0) return [];

        var path = $"schema/{schema.Name}/customProperties/{declarations[0].index}/value";
        try
        {
            if (declarations.Length > 1) throw Invalid("Declare packata indexes only once per schema object.");
            var declaration = Object(declarations[0].entry.ReadMappingValue(), "version", "indexes");
            if (!declaration.TryGetValue("version", out var version) || !IsVersionOne(version))
                throw Invalid("The indexes declaration requires integer version 1.");
            if (!declaration.TryGetValue("indexes", out var sequence))
                throw Invalid("The indexes declaration requires an indexes sequence.");

            var indexes = new List<DataIndex>();
            var seen = new HashSet<(string, DataIndexKind)>();
            foreach (var entry in Sequence(sequence))
            {
                var index = Entry(entry, schema);
                if (!seen.Add((index.Property, index.Kind)))
                    throw Invalid($"Duplicate {index.Kind} index for property '{index.Property}'.");
                indexes.Add(index);
            }

            return indexes.AsReadOnly();
        }
        catch (FormatException error)
        {
            diagnostics.Add(new("ODCS010", MappingSeverity.Error, path, error.Message));
            return [];
        }
    }

    private static DataIndex Entry(object? entry, SchemaObject schema)
    {
        var values = Object(entry, "property", "kind", "order", "comparison");
        var name = Text(values, "property");
        var properties = schema.Properties.Where(property => property.Name == name).ToArray();
        if (string.IsNullOrWhiteSpace(name) || properties.Length != 1)
            throw Invalid($"Index property '{name}' must name exactly one top-level property in '{schema.Name}'.");

        var kind = Text(values, "kind") switch
        {
            "ordered" => DataIndexKind.Ordered,
            "bloom" => DataIndexKind.Bloom,
            _ => throw Invalid("Index kind must be ordered or bloom.")
        };
        var order = Text(values, "order") switch
        {
            null when kind == DataIndexKind.Bloom => (DataIndexOrder?)null,
            null or "ascending" when kind == DataIndexKind.Ordered => DataIndexOrder.Ascending,
            "descending" when kind == DataIndexKind.Ordered => DataIndexOrder.Descending,
            _ => throw Invalid("Only ordered indexes accept order, which must be ascending or descending.")
        };
        var comparison = Text(values, "comparison") switch
        {
            null => (DataIndexComparison?)null,
            "ordinal" => DataIndexComparison.Ordinal,
            "ordinalIgnoreCase" => DataIndexComparison.OrdinalIgnoreCase,
            _ => throw Invalid("Index comparison must be ordinal or ordinalIgnoreCase.")
        };
        return new(name, kind, order, comparison);
    }

    private static bool IsVersionOne(object? value) => value switch
    {
        byte item => item == 1,
        sbyte item => item == 1,
        short item => item == 1,
        ushort item => item == 1,
        int item => item == 1,
        uint item => item == 1,
        long item => item == 1,
        ulong item => item == 1,
        JsonElement { ValueKind: JsonValueKind.Number } json => json.TryGetInt32(out var version) && version == 1,
        _ => false
    };

    private static IEnumerable<object?> Sequence(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray().Select(item => (object)item),
        IList list => list.Cast<object?>(),
        _ => throw Invalid("The indexes member must be a sequence.")
    };

    private static Dictionary<string, object?> Object(object? value, params string[] keys)
    {
        var members = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Object } json => json.EnumerateObject()
                .Select(member => ((object)member.Name, (object?)member.Value)),
            IDictionary dictionary => dictionary.Keys.Cast<object>().Select(key => (key, dictionary[key])),
            _ => throw Invalid("An indexes declaration or entry must be a mapping object.")
        };

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, member) in members)
        {
            if (key is not string name || !keys.Contains(name, StringComparer.Ordinal) || !result.TryAdd(name, member))
                throw Invalid($"Unexpected or duplicate index member '{key}'.");
        }
        return result;
    }

    private static string? Text(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw)) return null;
        return raw switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => throw Invalid($"Index member '{key}' must be a string.")
        };
    }

    private static FormatException Invalid(string message) => new(message);
}
