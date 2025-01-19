using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.CustomTypes;
internal class YearMonthParser
{
    public (int Year, int Month) Parse(string value)
    {
        var result = InternalParse(value);
        if (result.message is not null)
            throw new FormatException(result.message);
        else
            return (result.Year!.Value, result.Month!.Value);
    }

    public bool TryParse(string value, [NotNullWhen(true)] out int? year, [NotNullWhen(true)] out int? month)
    {
        var result = InternalParse(value);
        if (result.message is not null)
        {
            (year, month) = (null, null);
            return false;
        }
        (year, month) = (result.Year!.Value, result.Month!.Value);
        return true;
    }

    protected (int? Year, int? Month, string? message) InternalParse(string value)
    {
        var parts = value.Split('-');
        if (parts.Length != 2)
            return (null, null, "Invalid year-month format. Expecting a single dash.");
        if (parts[0].Length != 4 || !int.TryParse(parts[0], out var year))
            return (null, null, "Invalid year-month format. Invalid year.");
        if (parts[1].Length != 2 || !int.TryParse(parts[1], out var month))
            return (null, null, "Invalid year-month format. Invalid month.");
        return (year, month, null);
    }
}
