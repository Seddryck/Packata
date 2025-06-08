using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;
/// <summary>
/// Additional metadata options for a logical type "date".
/// </summary>
public class DateLogicalType : BaseLogicalType<DateTime>
{
    public DateLogicalType(Dictionary<string, object>? dict)
        : base(dict)
    { }

    protected override DateTime? ConvertValue(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? DateTime.TryParse(str, out var result) ? result : null : null;
}
