using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;

/// <summary>
/// A list of key/value pairs for custom properties. Initially created to support the REF ruleset property.
/// </summary>
public class CustomProperty
{
    /// <summary>
    /// Stable identifier for this custom property.
    /// </summary>
    /// <value>
    /// Stable identifier for this custom property.
    /// </value>
    [Label("ID")]
    public string? Id { get; set; }

    /// <summary>
    /// The name of the key. Names should be in camel case–the same as if they were permanent properties in the contract.
    /// </summary>
    /// <value>
    /// The name of the key. Names should be in camel case–the same as if they were permanent properties in the contract.
    /// </value>
    [Label("Property")]
    public string? Property { get; set; }

    /// <summary>
    /// The value of the key.
    /// </summary>
    /// <value>
    /// The value of the key.
    /// </value>
    [Label("Value")]
    public object? Value { get; set; }

    /// <summary>
    /// Human-readable description of the custom property.
    /// </summary>
    /// <value>
    /// Human-readable description of the custom property.
    /// </value>
    [Label("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Vendor, provider, or external system associated with the property.
    /// Unknown identifiers are intentionally preserved.
    /// </summary>
    /// <value>
    /// Vendor, provider, or external system associated with the property.
    /// Unknown identifiers are intentionally preserved.
    /// </value>
    [Label("Vendor")]
    public string? Vendor { get; set; }
}
