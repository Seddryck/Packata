using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class DremioServer : BaseServer, IHostAware, ISchemaAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    [Label("Host")]
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server.
    /// </summary>
    [Label("Port")]
    public required int Port { get; set; }

    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    [Label("Schema")]
    public required string Schema { get; set; }
}
