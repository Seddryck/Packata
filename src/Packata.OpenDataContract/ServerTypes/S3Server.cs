using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class S3Server : BaseServer, ILocationAware, IFormatAware, IEncodingAware
{
    /// <summary>
    /// S3 URL, starting with s3://
    /// </summary>
    /// <value>
    /// S3 URL, starting with s3://
    /// </value>
    [Label("Location")]
    public required string Location { get; set; }

    /// <summary>
    /// The server endpoint for S3-compatible servers.
    /// </summary>
    /// <value>
    /// The server endpoint for S3-compatible servers.
    /// </value>
    [Label("Endpoint URL")]
    public string? EndpointUrl { get; set; }

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
