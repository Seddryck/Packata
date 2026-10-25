using System.Collections;
using Newtonsoft.Json.Linq;

namespace Packata.DataPackage.Serialization;

internal static class InlineDataNormalizer
{
    public static object? Normalize(object? value) => value switch
    {
        null => null,
        JValue scalar => scalar.Value,
        JObject map => map.Properties().ToDictionary(
            property => property.Name, property => Normalize(property.Value)),
        IDictionary map => NormalizeDictionary(map),
        IEnumerable sequence when value is not string => NormalizeSequence(sequence),
        _ => value
    };

    private static IDictionary<string, object?> NormalizeDictionary(IDictionary map)
    {
        var values = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in map)
            values.Add((string)entry.Key, Normalize(entry.Value));
        return values;
    }

    private static object NormalizeSequence(IEnumerable sequence)
    {
        var values = sequence.Cast<object?>().Select(Normalize).ToArray();
        return values.All(value => value is IDictionary<string, object?>)
            ? values.Cast<IDictionary<string, object?>>().ToArray()
            : values;
    }
}
