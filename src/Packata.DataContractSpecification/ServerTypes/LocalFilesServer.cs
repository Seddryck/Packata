using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.DataContractSpecification.ServerTypes;
public class LocalFilesServer : BaseServer
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
