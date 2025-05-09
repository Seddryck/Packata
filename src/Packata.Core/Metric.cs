using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class Metric
{
    /// <summary>
    /// The name of the metric.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The type of the metric.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// A human-readable title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// A text description. Markdown is encouraged.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The aggregation function to be used for the metric (e.g. SUM, MAX, COUNT).
    /// </summary>
    public string? Aggregation { get; set; }

    /// <summary>
    /// The expression to aggregate the metric. It can be a simple field name or a more complex expression.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// The dimension(s) to be used for the metric. It can be a single field name or an array of field names.
    /// </summary>
    public List<string>? Dimensions { get; set; }
}
