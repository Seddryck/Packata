using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class AzureServer : BaseServer, ILocationAware, IFormatAware, IEncodingAware
{
    /// <summary>
    /// Fully qualified path to Azure Blob Storage or Azure Data Lake Storage (ADLS), supports globs.
    /// </summary>
    /// <value>
    /// Fully qualified path to Azure Blob Storage or Azure Data Lake Storage (ADLS), supports globs.
    /// </value>
    [Label("Location")]
    public required string Location { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    /// <value>
    /// File format.
    /// </value>
    [Label("Format")]
    public required string Format { get; set; }

    /// <summary>
    /// Only for format = json. How multiple json documents are delimited within one file
    /// </summary>
    /// <value>
    /// Only for format = json. How multiple json documents are delimited within one file
    /// </value>
    [Label("Delimiter")]
    public string? Delimiter { get; set; }

    public string Encoding { get; set; } = "UTF-8";
}
