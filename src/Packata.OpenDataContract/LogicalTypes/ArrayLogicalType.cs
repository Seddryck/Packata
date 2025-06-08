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

        UniqueItems = dict.GetValueOrDefault("uniqueItems") as bool? ?? false;
        MinItems = dict.GetValueOrDefault("minItems") as int?;
        MaxItems = dict.GetValueOrDefault("maxItems") as int?;
    }

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
