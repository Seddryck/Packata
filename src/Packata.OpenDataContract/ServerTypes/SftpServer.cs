using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class SftpServer : BaseServer, ILocationAware, IFormatAware
{
    /// <summary>
    /// S3 URL, starting with s3://
    /// </summary>
    [Label("Location")]
    public required string Location { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    [Label("Format")]
    public required string Format { get; set; }

    /// <summary>
    /// Only for format = json. How multiple json documents are delimited within one file
    /// </summary>
    [Label("Delimiter")]
    public string? Delimiter { get; set; }
}
