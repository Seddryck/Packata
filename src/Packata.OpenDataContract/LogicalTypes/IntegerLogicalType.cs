using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;
/// <summary>
/// Additional metadata options for a logical type "date".
/// </summary>
public class IntegerLogicalType : BaseLogicalType<int>
{
    public IntegerLogicalType(Dictionary<string, object>? dict)
        : base(dict)
    { }

    protected override int? ConvertValue(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? int.TryParse(str, out var result) ? result : null : null;
}
