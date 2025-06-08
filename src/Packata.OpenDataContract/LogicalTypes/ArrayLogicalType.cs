using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract;

/// <summary>
/// Additional metadata options for a logical type "array".
/// </summary>
public class ArrayLogicalType : ILogicalType
{
    public ArrayLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;

        UniqueItems = ConvertBool(dict.GetValueOrDefault("uniqueItems")) ?? false;
        MinItems = ConvertInt(dict.GetValueOrDefault("minItems"));
        MaxItems = ConvertInt(dict.GetValueOrDefault("maxItems"));
    }

    protected virtual int? ConvertInt(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? int.TryParse(str, out var result) ? result : null : null;

    protected virtual bool? ConvertBool(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? bool.TryParse(str, out var result) ? result : null : null;

    /// <summary>
    /// If set to true, all items in the array are unique.
    /// </summary>
    public bool UniqueItems { get; set; } = false;

    /// <summary>
    /// Maximum number of items.
    /// </summary>
    public int? MaxItems { get; set; }

    /// <summary>
    /// Minimum number of items.
    /// </summary>
    public int? MinItems { get; set; }
}
