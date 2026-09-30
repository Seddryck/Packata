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
    /// The title of the contributor.
    /// Example: "Jane Doe".
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The email address of the contributor.
    /// </summary>
    /// <example>
    /// "jane.doe@example.com"
    /// </example>
    public string? Email { get; set; }

    /// <summary>
    /// A fully qualified URL, or a POSIX file path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The given name of the contributor.
    /// Example: "Jane Doe".
    /// </summary>
    public string? GivenName { get; set; }

    /// <summary>
    /// The family name of the contributor.
    /// </summary>
    public string? FamilyName { get; set; }

    /// <summary>
    /// An organizational affiliation for this contributor.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// The role of the contributor in the data package.
    /// Example: "author".
    /// </summary>
    public List<string>? Roles { get; set; }
}
