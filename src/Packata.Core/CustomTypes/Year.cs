using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.CustomTypes;
public sealed record Year (int Value) : IEquatable<Year>, IParsable<Year>
{
    public static Year Parse(string s, IFormatProvider? provider)
        => s.Length == 4 ? new (int.Parse(s, provider)) : throw new FormatException();
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Year result)
    {
        if (s?.Length==4 && int.TryParse(s, provider, out var value))
        {
            result = new Year(value);
            return true;
        }
        result = null;
        return false;
    }

    public override string ToString() => Value.ToString();

    public bool Equals(Year? other)
        => other is not null && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
