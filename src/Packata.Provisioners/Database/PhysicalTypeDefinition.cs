using System.Text.RegularExpressions;
using DubUrl.Schema;

namespace Packata.Provisioners.Database;

internal sealed record PhysicalTypeDefinition(NativeDatabaseType NativeType, int? Length = null,
    int? Precision = null, int? Scale = null)
{
    private static readonly Regex TypePattern = new(
        @"^(?<name>[a-z][a-z0-9 ]*)(?:\((?<first>\d+)(?:\s*,\s*(?<second>\d+))?\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out PhysicalTypeDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = TypePattern.Match(value.Trim());
        if (!match.Success) return false;
        var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
        if (!TryNumber(match.Groups["first"], out var first)
            || !TryNumber(match.Groups["second"], out var second)) return false;
        if (first is <= 0 || second is < 0 || second > first) return false;

        try
        {
            var nativeType = new NativeDatabaseType(name);
            definition = second is not null
                ? new(nativeType, Precision: first, Scale: second)
                : first is not null
                    ? new(nativeType, Length: first)
                    : new(nativeType);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryNumber(Group group, out int? value)
    {
        value = null;
        if (!group.Success) return true;
        if (!int.TryParse(group.Value, out var parsed)) return false;
        value = parsed;
        return true;
    }
}
