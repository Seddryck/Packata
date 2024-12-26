using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents a contributor to the data package.
/// </summary>
public class Contributor
{
    /// <summary>
    /// The name of the contributor.
    /// Example: "Jane Doe".
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The email address of the contributor.
    /// Example: "jane.doe@example.com".
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The role of the contributor in the data package.
    /// Example: "author".
    /// </summary>
    public string? Role { get; set; }
}
