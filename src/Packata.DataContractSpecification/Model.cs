using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;
/// <summary>
/// Describes a logical data model such as a table or view.
/// </summary>
public class Model
{
    /// <summary>
    /// A human-readable name for the model.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Description of the model and its purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The type of model (e.g., table, view, object).
    /// </summary>
    public required string Type { get; set; } = "table";

    /// <summary>
    /// Fields in the model, keyed by field name.
    /// </summary>
    public Dictionary<string, Field> Fields { get; set; } = [];

    /// <summary>
    /// Additional configuration metadata for the model.
    /// </summary>
    public Dictionary<string, object> Config { get; set; } = [];
}
