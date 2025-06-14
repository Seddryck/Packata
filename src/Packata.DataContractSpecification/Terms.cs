using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;

/// <summary>
/// Represents the terms and conditions of the data contract.
/// </summary>
/// <summary>
/// Represents the terms and conditions of the data contract.
/// </summary>
public class Terms
{
    /// <summary>
    /// Describes how the data is expected to be used. Can include business and technical usage.
    /// </summary>
    public string? Usage { get; set; }

    /// <summary>
    /// Describes the restrictions on how the data can be used.
    /// May include technical limits or policy constraints.
    /// </summary>
    public string? Limitations { get; set; }

    /// <summary>
    /// A list of applicable policies governing privacy, security, retention, or compliance.
    /// </summary>
    public List<TermsPolicy> Policies { get; set; } = [];

    /// <summary>
    /// Describes the pricing model for using the data (e.g., free, monthly fee, pay-per-use).
    /// </summary>
    public string? Billing { get; set; }

    /// <summary>
    /// Period required to terminate or modify the data usage agreement, in ISO-8601 format (e.g., "P3M").
    /// </summary>
    public string? NoticePeriod { get; set; }

    /// <summary>
    /// Additional custom properties not defined in the schema.
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}
