using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for a logical type "string".
/// </summary>
public abstract class BaseLogicalType<T> : ILogicalType where T : struct
{
    protected BaseLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;

        if (dict.TryGetValue("format", out var fmt) && fmt is string fmtStr)
            Format = fmtStr;
        if (dict.TryGetValue("exclusiveMaximum", out var exclusiveMaximum) && exclusiveMaximum is string isExclusiveMaximum)
            ExclusiveMaximum = Convert.ToBoolean(isExclusiveMaximum);
        if (dict.TryGetValue("exclusiveMinimum", out var exclusiveMinimum) && exclusiveMinimum is string isExclusiveMinimum)
            ExclusiveMinimum = Convert.ToBoolean(isExclusiveMinimum);
        Maximum = ConvertValue(dict.GetValueOrDefault("maximum"));
        Minimum = ConvertValue(dict.GetValueOrDefault("minimum"));
    }

    protected abstract T? ConvertValue(object? value);

    /// <summary>
    /// Provides extra context about what format the string follows.
    /// For example: password, byte, binary, email, uuid, uri, hostname, ipv4, ipv6.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Maximum length of the string.
    /// </summary>
    public bool ExclusiveMaximum { get; set; } = false;

    /// <summary>
    /// If set to true, all values must be strictly greater than the minimum (value &gt; minimum).
    /// Otherwise, greater than or equal to the minimum (value ≥ minimum).
    /// </summary>
    public bool ExclusiveMinimum { get; set; } = false;

    /// <summary>
    /// All date values must be less than or equal to this maximum value.
    /// </summary>
    public T? Maximum { get; set; }

    /// <summary>
    /// All date values must be greater than or equal to this minimum value.
    /// </summary>
    public T? Minimum { get; set; }
}
