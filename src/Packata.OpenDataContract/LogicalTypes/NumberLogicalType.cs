﻿using System;
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
    { }

    protected override decimal? ConvertValue(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? decimal.TryParse(str, out var result) ? result : null : null;
}
