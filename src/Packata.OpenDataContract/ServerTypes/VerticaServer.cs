using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.ServerTypes;
public class VerticaServer : BaseServer, IHostAware, IDatabaseAware, ISchemaAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server.
    /// </summary>
    public required int Port { get; set; }

    /// <summary>
    /// The name of the database.
    /// </summary>
    public required string Database { get; set; }

    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    public required string Schema { get; set; }
}
