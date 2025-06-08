using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;

/// <summary>
/// List of links to sources that provide more details on the dataset; examples would be a link to an external definition, a training video, a git repo, data catalog, or another tool. Authoritative definitions follow the same structure in the standard.
/// </summary>
public class AuthoritativeDefinition
{
    /// <summary>
    /// URL to the authority.
    /// </summary>
    [Label("Url")]
    public string? Url { get; set; }

    /// <summary>
    /// Type of definition for authority: v2.3 adds standard values: `businessDefinition`, `transformationImplementation`, `videoTutorial`, `tutorial`, and `implementation`.
    /// </summary>
    [Label("Type")]
    public string? Type { get; set; }
}
