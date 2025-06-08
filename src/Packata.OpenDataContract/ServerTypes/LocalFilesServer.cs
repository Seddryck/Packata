using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class LocalFilesServer : BaseServer, IFormatAware
{
    /// <summary>
    /// The relative or absolute path to the data file(s).
    /// </summary>
    [Label("Path")]
    public required string Path { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    [Label("Format")]
    public required string Format { get; set; }
}
