using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class DatabricksServer : BaseServer, IHostAware, ICatalogAware, ISchemaAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server.
    /// </summary>
    public required int Port { get; set; } = 15001;

    /// <summary>
    /// The name of the catalog.
    /// </summary>
    public required string Catalog { get; set; }

    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    public required string Schema { get; set; }
}
