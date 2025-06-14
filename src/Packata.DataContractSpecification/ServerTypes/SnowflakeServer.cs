using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.DataContractSpecification.ServerTypes;
public class SnowflakeServer : BaseServer
{
    /// <summary>
    /// The Snowflake account used by the server.
    /// </summary>
    public required string Account { get; set; }

    /// <summary>
    /// The name of the database.
    /// </summary>
    public required string Database { get; set; }

    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    public required string Schema { get; set; }
}
