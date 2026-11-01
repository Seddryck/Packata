using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage;
public class Metric
{
    /// <summary>
    /// The name of the metric.
    /// </summary>
    /// <value>
    /// The name of the metric.
    /// </value>
    public string? Name { get; set; }

    /// <summary>
    /// The type of the metric.
    /// </summary>
    /// <value>
    /// The type of the metric.
    /// </value>
    public string? Type { get; set; }

    /// <summary>
    /// A human-readable title.
    /// </summary>
    /// <value>
    /// A human-readable title.
    /// </value>
    public string? Title { get; set; }

    /// <summary>
    /// A text description. Markdown is encouraged.
    /// </summary>
    /// <value>
    /// A text description. Markdown is encouraged.
    /// </value>
    public string? Description { get; set; }

    /// <summary>
    /// The aggregation function to be used for the metric (e.g. SUM, MAX, COUNT).
    /// </summary>
    /// <value>
    /// The aggregation function to be used for the metric (e.g. SUM, MAX, COUNT).
    /// </value>
    public string? Aggregation { get; set; }

    /// <summary>
    /// The expression to aggregate the metric. It can be a simple field name or a more complex expression.
    /// </summary>
    /// <value>
    /// The expression to aggregate the metric. It can be a simple field name or a more complex expression.
    /// </value>
    public string? Expression { get; set; }

    /// <summary>
    /// The dimension(s) to be used for the metric. It can be a single field name or an array of field names.
    /// </summary>
    /// <value>
    /// The dimension(s) to be used for the metric. It can be a single field name or an array of field names.
    /// </value>
    public List<string>? Dimensions { get; set; }
}
