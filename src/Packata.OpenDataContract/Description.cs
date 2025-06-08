using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;
public class Description
{
    /// <summary>
    /// Intended purpose for the provided data.
    /// </summary>
    [Label("Purpose")]
    public string? Purpose { get; set; }

    /// <summary>
    /// Technical, compliance, and legal limitations for data use.
    /// </summary>
    [Label("Limitations")]
    public string? Limitations { get; set; }

    /// <summary>
    /// Recommended usage of the data.
    /// </summary>
    [Label("Usage")]
    public string? Usage { get; set; }

    /// <summary>
    /// List of links to sources that provide more details on the dataset; examples would be a link to privacy statement, terms and conditions, license agreements, data catalog, or another tool.
    /// </summary>
    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    /// <summary>
    /// Custom properties that are not part of the standard.
    /// </summary>
    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
