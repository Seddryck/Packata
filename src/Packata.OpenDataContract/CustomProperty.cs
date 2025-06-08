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
    /// The name of the key. Names should be in camel case–the same as if they were permanent properties in the contract.
    /// </summary>
    [Label("Property")]
    public string? Property { get; set; }

    /// <summary>
    /// The value of the key.
    /// </summary>
    [Label("Value")]
    public string? Value { get; set; }
}
