using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
internal class PostgreSqlServer : BaseServer, IHostAware, IDatabaseAware, ISchemaAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    /// <value>
    /// The host to the database server
    /// </value>
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server. Default is 5432.
    /// </summary>
    /// <value>
    /// The port to the database server. Default is 5432.
    /// </value>
    public object? Port { get; set; } = 5432;

    /// <summary>
    /// The name of the database.
    /// </summary>
    /// <value>
    /// The name of the database.
    /// </value>
    [Label("Database")]
    public required string Database { get; set; }

    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    /// <value>
    /// The name of the schema in the database.
    /// </value>
    public required string Schema { get; set; }
}
