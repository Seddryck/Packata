using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;
public class DataContract
{
    /// <summary>
    /// Specifies the Data Contract Specification being used.
    /// Allowed values: "0.9.3", "0.9.2", "0.9.1", "0.9.0".
    /// </summary>
    public string DataContractSpecification { get; set; }

    /// <summary>
    /// Specifies the identifier of the data contract.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Metadata and life cycle information about the data contract.
    /// </summary>
    public Info? Info { get; set; }

    /// <summary>
    /// The terms and conditions of the data contract.
    /// </summary>
    public Terms? Terms { get; set; }

    /// <summary>
    /// Logical data models, keyed by model name (e.g., table name).
    /// </summary>
    public Dictionary<string, Model> Models { get; set; } = new();

    /// <summary>
    /// Logical data models, keyed by model name (e.g., table name).
    /// </summary>
    public Dictionary<string, BaseServer> Servers { get; set; } = new();
}
