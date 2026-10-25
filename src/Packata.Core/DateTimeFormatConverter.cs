using System.Text;

namespace Packata.Core;

/// <summary>Converts strptime temporal patterns to .NET patterns.</summary>
public static class DateTimeFormatConverter
{
    private static readonly Dictionary<string, string> FormatMap = new()
    {
        ["%a"] = "ddd", ["%A"] = "dddd", ["%b"] = "MMM", ["%B"] = "MMMM",
        ["%c"] = "F", ["%d"] = "dd", ["%-d"] = "d", ["%H"] = "HH", ["%-H"] = "H",
        ["%I"] = "hh", ["%m"] = "MM", ["%-m"] = "M", ["%M"] = "mm", ["%-M"] = "m",
        ["%p"] = "tt", ["%S"] = "ss", ["%-S"] = "s", ["%f"] = "ffffff",
        ["%x"] = "d", ["%X"] = "T", ["%y"] = "yy", ["%Y"] = "yyyy",
        ["%z"] = "zzz", ["%:z"] = "zzz"
    };

    public static bool HasOffset(string? pattern) =>
        pattern is not null && Tokens(pattern).Any(token => token is "%z" or "%:z");

    public static string Convert(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var result = new StringBuilder();
        foreach (var token in Tokens(pattern))
        {
            if (FormatMap.TryGetValue(token, out var replacement))
                result.Append(replacement);
            else if (token == "%%")
                result.Append("'%'");
            else if (token.StartsWith('%'))
                throw new NotSupportedException($"Unsupported strptime directive '{token}'.");
            else
                result.Append('\\').Append(token);
        }
        return result.ToString();
    }

    private static IEnumerable<string> Tokens(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var start = i;
            if (pattern[i] == '%' && i + 1 < pattern.Length)
            {
                i++;
                if (pattern[i] is ':' or '-' && i + 1 < pattern.Length) i++;
            }
            yield return pattern[start..(i + 1)];
        }
    }
}
