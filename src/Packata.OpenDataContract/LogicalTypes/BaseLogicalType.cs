using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for a logical type "string".
/// </summary>
public abstract class BaseLogicalType<T> : ILogicalType
    where T : struct
{
    protected BaseLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;

        if (dict.TryGetValue("format", out var fmt) && fmt is string fmtStr)
            Format = fmtStr;
        Maximum = ConvertValue(dict.GetValueOrDefault("maximum"));
        Minimum = ConvertValue(dict.GetValueOrDefault("minimum"));
        (Minimum, ExclusiveMinimum) = Bound(dict, "exclusiveMinimum", Minimum, minimum: true);
        (Maximum, ExclusiveMaximum) = Bound(dict, "exclusiveMaximum", Maximum, minimum: false);
    }

    private (T? Value, bool Exclusive) Bound(Dictionary<string, object> options, string key, T? inclusive, bool minimum)
    {
        if (!options.TryGetValue(key, out var raw)) return (inclusive, false);
        // Retain legacy native-model boolean options; descriptor schema validation rejects them.
        if (raw is bool boolean) return (inclusive, boolean);
        if (raw is string text && bool.TryParse(text, out var flag)) return (inclusive, flag);
        var exclusive = ConvertValue(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture));
        if (exclusive is null) return (inclusive, false);
        var comparison = inclusive is null ? 0 : Comparer<T>.Default.Compare(exclusive.Value, inclusive.Value);
        var tighter = comparison <= 0;
        if (minimum) tighter = comparison >= 0;
        return inclusive is null || tighter
            ? (exclusive, true) : (inclusive, false);
    }

    protected abstract T? ConvertValue(object? value);

    /// <summary>
    /// Provides extra context about what format the string follows.
    /// For example: password, byte, binary, email, uuid, uri, hostname, ipv4, ipv6.
    /// </summary>
    /// <value>
    /// Provides extra context about what format the string follows.
    /// For example: password, byte, binary, email, uuid, uri, hostname, ipv4, ipv6.
    /// </value>
    public string? Format { get; set; }

    /// <summary>
    /// Maximum length of the string.
    /// </summary>
    /// <value>
    /// Maximum length of the string.
    /// </value>
    public bool ExclusiveMaximum { get; set; } = false;

    /// <summary>
    /// If set to true, all values must be strictly greater than the minimum (value &gt; minimum).
    /// Otherwise, greater than or equal to the minimum (value ≥ minimum).
    /// </summary>
    /// <value>
    /// If set to true, all values must be strictly greater than the minimum (value &gt; minimum).
    /// Otherwise, greater than or equal to the minimum (value ≥ minimum).
    /// </value>
    public bool ExclusiveMinimum { get; set; } = false;

    /// <summary>
    /// All date values must be less than or equal to this maximum value.
    /// </summary>
    /// <value>
    /// All date values must be less than or equal to this maximum value.
    /// </value>
    public T? Maximum { get; set; }

    /// <summary>
    /// All date values must be greater than or equal to this minimum value.
    /// </summary>
    /// <value>
    /// All date values must be greater than or equal to this minimum value.
    /// </value>
    public T? Minimum { get; set; }
}
