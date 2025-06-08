using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;

public class DataContract
{
    /// <summary>
    /// Version of the standard used to build data contract. Default value is v3.0.2.
    /// </summary>
    [Label("Standard version")]
    public string ApiVersion { get; set; } = "v3.0.2";

    /// <summary>
    /// The kind of file this is. Valid value is DataContract.
    /// </summary>
    [Label("Kind")]
    public string Kind { get; set; } = "DataContract";

    /// <summary>
    /// A unique identifier used to reduce the risk of dataset name collisions, such as a UUID.
    /// </summary>
    [Label("ID")]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Name of the data contract.
    /// </summary>
    [Label("Name")]
    public string? Name { get; set; }

    /// <summary>
    /// Current version of the data contract.
    /// </summary>
    [Label("Version")]
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current status of the data contract. Examples are "proposed", "draft", "active", "deprecated", "retired".
    /// </summary>
    [Label("Status")]
    public string Status { get; set; } = default!;

    /// <summary>
    /// Indicates the property the data is primarily associated with. Value is case insensitive.
    /// </summary>
    [Label("Tenant")]
    public string? Tenant { get; set; }

    /// <summary>
    /// Name of the logical data domain.
    /// </summary>
    [Label("Domain")]
    public string? Domain { get; set; }

    /// <summary>
    /// Name of the data product.
    /// </summary>
    [Label("Data Product")]
    public string? DataProduct { get; set; }

    /// <summary>
    /// List of links to sources that provide more details on the data contract.
    /// </summary>
    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    /// <summary>
    /// Object containing the descriptions.
    /// </summary>
    [Label("Description")]
    public Description? Description { get; set; }

    /// <summary>
    /// A list of tags that may be assigned to the dataContract. Tags are used to categorize the data contract and can be used for searching and filtering.
    /// </summary>
    [Label("Tags")]
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// A list of elements within the schema to be cataloged.
    /// </summary>
    [Label("Schema")]
    public List<SchemaObject> Schema { get; set; } = [];

    /// <summary>
    /// The servers element describes where the data protected by this data contract is physically located.
    /// That metadata helps to know where the data is so that a data consumer can discover the data and a platform engineer can automate access.
    /// </summary>
    [Label("Servers")]
    public List<BaseServer> Servers { get; set; } = [];
}
