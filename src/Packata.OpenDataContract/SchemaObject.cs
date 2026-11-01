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
    /// <value>
    /// The logical element data type.
    /// </value>
    [Label("Logical Type")]
    public string LogicalType { get => "object"; }

    /// <summary>
    /// Physical name.
    /// </summary>
    /// <value>
    /// Physical name.
    /// </value>
    [Label("Physical Name")]
    public string? PhysicalName { get; set; }

    /// <summary>
    /// Granular level of the data in the object. Example would be "Aggregation by country."
    /// </summary>
    /// <value>
    /// Granular level of the data in the object. Example would be "Aggregation by country."
    /// </value>
    [Label("Data Granularity")]
    public string? DataGranularityDescription { get; set; }

    /// <summary>
    /// A list of properties for the object.
    /// </summary>
    /// <value>
    /// A list of properties for the object.
    /// </value>
    [Label("Properties")]
    public List<SchemaProperty> Properties { get; set; } = [];

    /// <summary>
    /// Data quality checks applied to the schema object.
    /// </summary>
    /// <value>
    /// Data quality checks applied to the schema object.
    /// </value>
    [Label("Quality")]
    public List<DataQualityRule> Quality { get; set; } = [];

    /// <summary>
    /// Relationships between schema elements.
    /// </summary>
    /// <value>
    /// Relationships between schema elements.
    /// </value>
    [Label("Relationships")]
    public List<Relationship> Relationships { get; set; } = [];

    /// <summary>
    /// AI and semantic guidance applying to this schema object.
    /// </summary>
    /// <value>
    /// AI and semantic guidance applying to this schema object.
    /// </value>
    [Label("Context")]
    public DataContractContext? Context { get; set; }
}
