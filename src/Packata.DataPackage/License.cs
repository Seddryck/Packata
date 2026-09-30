using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents a license for the data package descriptor.
/// </summary>
public class License
{
    /// <summary>
    /// Open Definition license identifier.
    /// MUST be an Open Definition license identifier, see http://licenses.opendefinition.org/
    /// </summary>
    /// <example>
    /// CC0-1.0
    /// </example>
    [RegularExpression("^([-a-zA-Z0-9._])+$", ErrorMessage = "Invalid Open Definition license identifier.")]
    public string? Name { get; set; }

    /// <summary>
    /// Value to the license.
    /// A fully qualified URL, or a POSIX file path.
    /// Context: Implementations need to negotiate the type of path provided, and dereference the data accordingly.
    /// </summary>
    /// <example>
    /// { "path": "license.txt" }
    /// { "path": "https://creativecommons.org/publicdomain/zero/1.0/" }
    /// </example>
    public string? Path { get; set; }

    /// <summary>
    /// A human-readable title for the license.
    /// Example: { "title": "My License Title" }
    /// </summary>
    /// <example>
    /// CC0 1.0
    /// </example>
    public string? Title { get; set; }
}
