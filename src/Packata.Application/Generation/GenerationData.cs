using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace Packata.Application.Generation;

internal static class GenerationData
{
    internal static Dictionary<string, object?> Object(params (string Key, object? Value)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    internal static object? Plain(object? value) => value is null ? null : Token(ContractJson.RedactedJson(value));

    private static object? Token(JToken token) => token switch
    {
        JObject obj => obj.Properties().ToDictionary(p => p.Name, p => Token(p.Value), StringComparer.Ordinal),
        JArray array => array.Select(Token).ToArray(),
        JValue scalar => scalar.Value,
        _ => null
    };

    internal static string Literal(string? text) => JsonConvert.ToString(ContractJson.Redact(text ?? ""), '"', StringEscapeHandling.EscapeNonAscii);

    internal static IReadOnlyDictionary<string, object?> Variables(IReadOnlyList<string>? definitions)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var definition in definitions ?? [])
        {
            var separator = definition.IndexOf('=');
            if (separator < 1) throw new ArgumentException("Each --define must have a non-empty key followed by =value.");
            var key = definition[..separator];
            if (!result.TryAdd(key, Value(definition[(separator + 1)..])))
                throw new ArgumentException($"Duplicate user variable '{key}'. Use each --define key once.");
        }
        return result;
    }

    private static object? Value(string text)
    {
        if (text == "null") return null;
        if (bool.TryParse(text, out var boolean)) return boolean;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return text;
    }
}
