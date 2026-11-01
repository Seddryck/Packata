using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class LocalFilesServer : BaseServer, IFormatAware, IEncodingAware
{
    /// <summary>
    /// The relative or absolute path to the data file(s).
    /// </summary>
    /// <value>
    /// The relative or absolute path to the data file(s).
    /// </value>
    [Label("Path")]
    public required string Path { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    /// <value>
    /// File format.
    /// </value>
    [Label("Format")]
    public required string Format { get; set; }

    public string Encoding { get; set; } = "UTF-8";
}
