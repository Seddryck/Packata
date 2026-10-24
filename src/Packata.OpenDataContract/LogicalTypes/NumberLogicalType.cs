using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;
/// <summary>
/// Additional metadata options for a logical type "date".
/// </summary>
public class NumberLogicalType : BaseLogicalType<decimal>
{
    public NumberLogicalType(Dictionary<string, object>? dict)
        : base(dict)
    {
        Precision = ReadInteger(dict, "precision");
        Scale = ReadInteger(dict, "scale");
    }

    public int? Precision { get; set; }
    public int? Scale { get; set; }

    private static int? ReadInteger(Dictionary<string, object>? dict, string name) =>
        int.TryParse(Convert.ToString(dict?.GetValueOrDefault(name), System.Globalization.CultureInfo.InvariantCulture), out var value) ? value : null;

    protected override decimal? ConvertValue(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? decimal.TryParse(str, out var result) ? result : null : null;
}

