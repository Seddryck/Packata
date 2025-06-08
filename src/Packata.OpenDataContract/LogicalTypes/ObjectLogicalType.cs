using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract;

/// <summary>
/// Additional metadata options for a logical type "object".
/// </summary>
public class ObjectLogicalType : ILogicalType
{
    public ObjectLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;

        Required = dict.GetValueOrDefault("required") as string[] ?? [];
        MinProperties = dict.GetValueOrDefault("minProperties") as int?;
        MaxProperties = dict.GetValueOrDefault("maxProperties") as int?;
    }

    /// <summary>
    /// Property names that are required to exist in the object.
    /// </summary>
    public string[] Required { get; set; } = [];

    /// <summary>
    /// Maximum number of properties.
    /// </summary>
    public int? MaxProperties { get; set; }

    /// <summary>
    /// Minimum number of properties.
    /// </summary>
    public int? MinProperties { get; set; }
}
