using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;

/// <summary>
/// Represents the raw sources for this resource.
/// </summary>
public class Source
{
    /// <summary>
    /// A human-readable title.
    /// </summary>
    /// <example>
    /// My Source Title
    /// </example>
    public string? Title { get; set; }

    /// <summary>
    /// A fully qualified URL, or a POSIX file path.
    /// Context: Implementations need to negotiate the type of path provided, and dereference the data accordingly.
    /// </summary>
    /// <example>
    /// file.csv
    /// http://example.com/file.csv
    /// </example>
    public string? Path { get; set; }

    /// <summary>
    /// An email address.
    /// </summary>
    /// <example>
    /// example@example.com
    /// </example>
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string? Email { get; set; }
}
