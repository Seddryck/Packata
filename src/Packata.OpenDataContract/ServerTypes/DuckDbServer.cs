using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class DuckDbServer : BaseServer, IDatabaseAware, ISchemaAware
{
    /// <summary>
    /// Path to duckdb database file.
    /// </summary>
    [Label("Database")]
    public required string Database { get; set; }

    /// <summary>
    /// The name of the schema.
    /// </summary>
    [Label("Schema")]
    public required string Schema { get; set; }
}
