using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.DataContractSpecification.ServerTypes;
public class SftpServer : BaseServer
{
    /// <summary>
    /// SFTP URL, starting with sftp://. The URL should include the port number.
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
