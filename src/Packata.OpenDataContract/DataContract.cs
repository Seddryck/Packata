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
    /// Version of the standard used to build data contract. Default value is v3.2.0.
    /// </summary>
    /// <value>
    /// Version of the standard used to build data contract. Default value is v3.2.0.
    /// </value>
    [Label("Standard version")]
    public string ApiVersion { get; set; } = "v3.2.0";

    /// <summary>
    /// The kind of file this is. Valid value is DataContract.
    /// </summary>
    /// <value>
    /// The kind of file this is. Valid value is DataContract.
    /// </value>
    [Label("Kind")]
    public string Kind { get; set; } = "DataContract";

    /// <summary>
    /// A unique identifier used to reduce the risk of dataset name collisions, such as a UUID.
    /// </summary>
    /// <value>
    /// A unique identifier used to reduce the risk of dataset name collisions, such as a UUID.
    /// </value>
    [Label("ID")]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Name of the data contract.
    /// </summary>
    /// <value>
    /// Name of the data contract.
    /// </value>
    [Label("Name")]
    public string? Name { get; set; }

    /// <summary>
    /// Current version of the data contract.
    /// </summary>
    /// <value>
    /// Current version of the data contract.
    /// </value>
    [Label("Version")]
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current status of the data contract. Examples are "proposed", "draft", "active", "deprecated", "retired".
    /// </summary>
    /// <value>
    /// Current status of the data contract. Examples are "proposed", "draft", "active", "deprecated", "retired".
    /// </value>
    [Label("Status")]
    public string Status { get; set; } = default!;

    /// <summary>
    /// Indicates the property the data is primarily associated with. Value is case insensitive.
    /// </summary>
    /// <value>
    /// Indicates the property the data is primarily associated with. Value is case insensitive.
    /// </value>
    [Label("Tenant")]
    public string? Tenant { get; set; }

    /// <summary>
    /// Name of the logical data domain.
    /// </summary>
    /// <value>
    /// Name of the logical data domain.
    /// </value>
    [Label("Domain")]
    public string? Domain { get; set; }

    /// <summary>
    /// Name of the data product.
    /// </summary>
    /// <value>
    /// Name of the data product.
    /// </value>
    [Label("Data Product")]
    public string? DataProduct { get; set; }

    /// <summary>
    /// List of links to sources that provide more details on the data contract.
    /// </summary>
    /// <value>
    /// List of links to sources that provide more details on the data contract.
    /// </value>
    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    /// <summary>
    /// Custom properties attached to the contract.
    /// </summary>
    /// <value>
    /// Custom properties attached to the contract.
    /// </value>
    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];

    /// <summary>
    /// AI and semantic guidance applying to the complete contract.
    /// </summary>
    /// <value>
    /// AI and semantic guidance applying to the complete contract.
    /// </value>
    [Label("Context")]
    public DataContractContext? Context { get; set; }

    /// <summary>
    /// Object containing the descriptions.
    /// </summary>
    /// <value>
    /// Object containing the descriptions.
    /// </value>
    [Label("Description")]
    public Description? Description { get; set; }

    /// <summary>
    /// A list of tags that may be assigned to the dataContract. Tags are used to categorize the data contract and can be used for searching and filtering.
    /// </summary>
    /// <value>
    /// A list of tags that may be assigned to the dataContract. Tags are used to categorize the data contract and can be used for searching and filtering.
    /// </value>
    [Label("Tags")]
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// A list of elements within the schema to be cataloged.
    /// </summary>
    /// <value>
    /// A list of elements within the schema to be cataloged.
    /// </value>
    [Label("Schema")]
    public List<SchemaObject> Schema { get; set; } = [];

    /// <summary>
    /// The servers element describes where the data protected by this data contract is physically located.
    /// That metadata helps to know where the data is so that a data consumer can discover the data and a platform engineer can automate access.
    /// </summary>
    /// <value>
    /// The servers element describes where the data protected by this data contract is physically located.
    /// That metadata helps to know where the data is so that a data consumer can discover the data and a platform engineer can automate access.
    /// </value>
    [Label("Servers")]
    public List<BaseServer> Servers { get; set; } = [];

    /// <summary>
    /// Support and communication channels for consumers.
    /// </summary>
    /// <value>
    /// Support and communication channels for consumers.
    /// </value>
    [Label("Support")]
    public List<SupportChannel> Support { get; set; } = [];

    /// <summary>
    /// Optional pricing information for consuming the data product.
    /// </summary>
    /// <value>
    /// Optional pricing information for consuming the data product.
    /// </value>
    [Label("Price")]
    public Price? Price { get; set; }

    /// <summary>
    /// Team responsible for the contract.
    /// </summary>
    /// <value>
    /// Team responsible for the contract.
    /// </value>
    [Label("Team")]
    public Team? Team { get; set; }

    /// <summary>
    /// IAM roles that provide access to the dataset.
    /// </summary>
    /// <value>
    /// IAM roles that provide access to the dataset.
    /// </value>
    [Label("Roles")]
    public List<AccessRole> Roles { get; set; } = [];

    /// <summary>
    /// Service-level agreement entries.
    /// </summary>
    /// <value>
    /// Service-level agreement entries.
    /// </value>
    [Label("SLA")]
    public List<ServiceLevelAgreementProperty> SlaProperties { get; set; } = [];

    /// <summary>
    /// Deprecated ODCS v3 default element retained for read compatibility.
    /// </summary>
    /// <value>
    /// Deprecated ODCS v3 default element retained for read compatibility.
    /// </value>
    [Obsolete("Use the element on each SLA property instead.")]
    public string? SlaDefaultElement { get; set; }
}
