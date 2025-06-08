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

        Required = (dict.GetValueOrDefault("required") as List<object> ?? []).Select(x => x.ToString() ?? string.Empty).ToArray();
        MinProperties = ConvertInt(dict.GetValueOrDefault("minProperties"));
        MaxProperties = ConvertInt(dict.GetValueOrDefault("maxProperties"));
    }

    private static int? ConvertInt(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? int.TryParse(str, out var result) ? result : null : null;

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
