using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class S3Server : BaseServer, ILocationAware, IFormatAware
{
    /// <summary>
    /// S3 URL, starting with s3://
    /// </summary>
    [Label("Location")]
    public required string Location { get; set; }

    /// <summary>
    /// The server endpoint for S3-compatible servers.
    /// </summary>
    [Label("Endpoint URL")]
    public string? EndpointUrl { get; set; }

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
