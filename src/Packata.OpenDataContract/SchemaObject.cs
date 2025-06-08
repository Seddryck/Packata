using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;
public class SchemaObject : SchemaElement
{
    /// <summary>
    /// The logical element data type.
    /// </summary>
    [Label("Logical Type")]
    public string LogicalType { get => "object"; }

    /// <summary>
    /// Physical name.
    /// </summary>
    [Label("Physical Name")]
    public string? PhysicalName { get; set; }

    /// <summary>
    /// Granular level of the data in the object. Example would be "Aggregation by country."
    /// </summary>
    [Label("Data Granularity")]
    public string? DataGranularityDescription { get; set; }

    /// <summary>
    /// A list of properties for the object.
    /// </summary>
    [Label("Properties")]
    public List<SchemaProperty> Properties { get; set; } = [];
}
