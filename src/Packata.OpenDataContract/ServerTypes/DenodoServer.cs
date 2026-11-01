using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class DenodoServer : BaseServer, IHostAware, IDatabaseAware
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
    /// The name of the database.
    /// </summary>
    /// <value>
    /// The name of the database.
    /// </value>
    [Label("Database")]
    public required string Database { get; set; }
}
