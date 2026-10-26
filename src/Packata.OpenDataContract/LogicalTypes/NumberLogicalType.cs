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
        if (Precision <= 0 || Scale < 0 || Scale > Precision)
            throw new YamlDotNet.Core.YamlException("Invalid decimal precision or scale.");
    }

    public int? Precision { get; set; }
    public int? Scale { get; set; }

    private static int? ReadInteger(Dictionary<string, object>? dict, string name)
    {
        if (dict?.TryGetValue(name, out var value) != true) return null;
        if (!int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var integer))
            throw new YamlDotNet.Core.YamlException("Decimal parameters must be integers.");
        return integer;
    }
    protected override decimal? ConvertValue(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? decimal.TryParse(str, out var result) ? result : null : null;
}
