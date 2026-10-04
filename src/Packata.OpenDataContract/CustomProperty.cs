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
    [Label("ID")]
    public string? Id { get; set; }

    /// <summary>
    /// The name of the key. Names should be in camel case–the same as if they were permanent properties in the contract.
    /// </summary>
    [Label("Property")]
    public string? Property { get; set; }

    /// <summary>
    /// The value of the key.
    /// </summary>
    [Label("Value")]
    public object? Value { get; set; }

    /// <summary>
    /// Human-readable description of the custom property.
    /// </summary>
    [Label("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Vendor, provider, or external system associated with the property.
    /// Unknown identifiers are intentionally preserved.
    /// </summary>
    [Label("Vendor")]
    public string? Vendor { get; set; }
}
