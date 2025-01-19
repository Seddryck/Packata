using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

namespace Packata.Core.CustomTypes;
public sealed record YearMonth(int Year, int Month) : IEquatable<YearMonth>, IParsable<YearMonth>
{
    private static readonly YearMonthParser _parser = new();

    public static YearMonth Parse(string s, IFormatProvider? provider)
    {
        var value = _parser.Parse(s);
        return new YearMonth(value.Year, value.Month);
    }

    public static bool TryParse([NotNullWhen(true)] string? value, IFormatProvider? provider, [NotNullWhen(true)] out YearMonth? result)
    {
        if (!string.IsNullOrEmpty(value) && _parser.TryParse(value, out var year, out var month))
        {
            result = new YearMonth(year!.Value, month!.Value);
            return true;
        }
        result = null;
        return false;
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    public bool Equals(YearMonth? other)
        => other is not null && Year == other.Year && Month == other.Month;
    public override int GetHashCode() => Year.GetHashCode() ^ 37 * Month.GetHashCode() ^ 17;
}
