using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage;
/// <summary>
/// Represents a contributor to the data package.
/// </summary>
public class Contributor
{
    /// <summary>
    /// The title of the contributor.
    /// Example: "Jane Doe".
    /// </summary>
    /// <value>
    /// The title of the contributor.
    /// Example: "Jane Doe".
    /// </value>
    public string? Title { get; set; }

    /// <summary>
    /// The email address of the contributor.
    /// </summary>
    /// <example>
    /// "jane.doe@example.com"
    /// </example>
    /// <value>
    /// The email address of the contributor.
    /// </value>
    public string? Email { get; set; }

    /// <summary>
    /// A fully qualified URL, or a POSIX file path.
    /// </summary>
    /// <value>
    /// A fully qualified URL, or a POSIX file path.
    /// </value>
    public string? Path { get; set; }

    /// <summary>
    /// The given name of the contributor.
    /// Example: "Jane Doe".
    /// </summary>
    /// <value>
    /// The given name of the contributor.
    /// Example: "Jane Doe".
    /// </value>
    public string? GivenName { get; set; }

    /// <summary>
    /// The family name of the contributor.
    /// </summary>
    /// <value>
    /// The family name of the contributor.
    /// </value>
    public string? FamilyName { get; set; }

    /// <summary>
    /// An organizational affiliation for this contributor.
    /// </summary>
    /// <value>
    /// An organizational affiliation for this contributor.
    /// </value>
    public string? Organization { get; set; }

    /// <summary>
    /// The role of the contributor in the data package.
    /// Example: "author".
    /// </summary>
    /// <value>
    /// The role of the contributor in the data package.
    /// Example: "author".
    /// </value>
    public List<string>? Roles { get; set; }
}
