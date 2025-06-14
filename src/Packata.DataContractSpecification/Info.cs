using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;

/// <summary>
/// Metadata and life cycle information about the data contract.
/// </summary>
public class Info
{
    /// <summary>
    /// The title of the data contract.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// The version of the data contract document (not the spec version or implementation version).
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The status of the data contract.
    /// Possible values: proposed, in development, active, deprecated, retired.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// A description of the data contract.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The owner or team responsible for the contract and data provision.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Contact information for the data contract.
    /// </summary>
    public Contact? Contact { get; set; }
}
