using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
internal class DateTimeFormatConverter
{
    // Mapping dictionary for C-style to .NET-style format specifiers
    private static readonly Dictionary<string, string> FormatMap = new()
    {
        { "%a", "ddd" }, //Abbreviated day of week
        { "%A", "dddd" }, //Full label of day of week
        { "%b", "MMM" }, //Abbreviated month
        { "%B", "MMMM" }, //Full label of Month
        { "%c", "F" },
        { "%d", "dd" }, //Day of month
        { "%-d", "d" },
        { "%H", "HH" }, //Hour
        { "%-H", "H" },
        { "%I", "hh" },
        { "%j", "ddd" }, // Day of year
        { "%m", "MM" }, //Month
        { "%-m", "M" },
        { "%M", "mm" }, //Minute
        { "%-M", "m" },
        { "%p", "tt" },
        { "%q", "q" }, // Non-standard for quarter
        { "%s", "S" }, // Non-standard for semester
        { "%S", "ss" }, //second
        { "%-S", "s" }, //second
        { "%U", "ww" }, // Week of year (Sunday-start)
        { "%w", "d" },  // Day of week (0-Sunday to 6)
        { "%W", "ww" }, // Week of year (Monday-start)
        { "%x", "d" },  // Date
        { "%X", "T" },  // Time
        { "%y", "yy" }, // Year
        { "%Y", "yyyy" },
        { "%z", "z00" },
        { "%:z", "zzz" },
        { "%%", "%" }  // Literal %
    };

    /// <summary>
    /// Transforms a C-style format string into a .NET-style format string.
    /// </summary>
    /// <param name="cStyleFormat">The C-style format string.</param>
    /// <returns>The equivalent .NET-style format string.</returns>
    public string Convert(string cStyleFormat)
    {
        if (string.IsNullOrEmpty(cStyleFormat))
            throw new ArgumentException("Format string cannot be null or empty.", nameof(cStyleFormat));

        foreach (var entry in FormatMap)
            cStyleFormat = cStyleFormat.Replace(entry.Key, entry.Value);

        return cStyleFormat;
    }
}
