using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification.ServerTypes;
public class S3Server : BaseServer
{
    /// <summary>
    /// S3 URL, starting with s3://
    /// </summary>
    public required string Location { get; set; }

    /// <summary>
    /// The server endpoint for S3-compatible servers, such as MioIO or Google Cloud Storage, e.g., https://minio.example.com
    /// </summary>
    public required string EndpointUrl { get; set; }

    /// <summary>
    /// The name of the bucket.
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// (Only for format = json), how multiple json documents are delimited within one file, e.g., new_line, array
    /// </summary>
    public string? Delimiter { get; set; }
}
