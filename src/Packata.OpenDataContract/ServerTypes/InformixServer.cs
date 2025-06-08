using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.ServerTypes;
public class InformixServer : BaseServer, IHostAware, IDatabaseAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server. Default is 9088.
    /// </summary>
    public required int Port { get; set; } = 9088;

    /// <summary>
    /// The name of the database.
    /// </summary>
    public required string Database { get; set; }
}
