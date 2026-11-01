using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class OracleServer : BaseServer, IHostAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    /// <value>
    /// The host to the database server
    /// </value>
    [Label("Host")]
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server.
    /// </summary>
    /// <value>
    /// The port to the database server.
    /// </value>
    [Label("Port")]
    public object? Port { get; set; }

    /// <summary>
    /// The name of the service.
    /// </summary>
    /// <value>
    /// The name of the service.
    /// </value>
    [Label("Service Name")]
    public required string ServiceName { get; set; }
}
