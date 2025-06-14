using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;

/// <summary>
/// Represents a specific policy related to the data contract's terms.
/// </summary>
public class TermsPolicy
{
    /// <summary>
    /// The type of the policy (e.g., privacy, security, retention, compliance).
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// A description of the policy.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// A URL to the full policy document.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Additional fields not covered by the schema.
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; } = [];
}
