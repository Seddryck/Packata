using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

internal static class AvroEnumProjection
{
    internal static IReadOnlyList<string>? Symbols(DataField field)
    {
        if (!string.Equals(field.LogicalType, "string", StringComparison.OrdinalIgnoreCase)) return null;
        var declarations = field.Constraints.Where(c => c.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (declarations.Length != 1) return null;
        var values = JToken.FromObject(declarations[0].Value ?? new object());
        if (values is not JArray { Count: > 0 } items || items.Any(v => v.Type != JTokenType.String)) return null;
        var symbols = items.Values<string>().Select(v => v!).ToArray();
        return symbols.Distinct(StringComparer.Ordinal).Count() == symbols.Length && symbols.All(IsSymbol) ? symbols : null;
    }

    private static bool IsSymbol(string value)
        => Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
