using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class LabelAttribute : Attribute
{
    /// <summary>
    /// Get the UI label for the property.
    /// </summary>
    public string Label { get; }
    /// <summary>
    /// Initializes a new instance of the <see cref="LabelAttribute"/> class with the specified label.
    /// </summary>
    /// <param name="label">The label for the property.</param>
    public LabelAttribute(string label)
    {
        Label = label;
    }
}
