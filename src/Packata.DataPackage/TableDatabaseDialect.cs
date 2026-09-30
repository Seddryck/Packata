using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents the table dialect as defined by the Data Package Table Dialect profile for the type "database".
/// </summary>
public class TableDatabaseDialect : TableDialect
{
    /// <summary>
    /// The name of the table in the database.
    /// </summary>
    public string? Table { get; set; }

    /// <summary>
    /// The name of the database schema (namespace).
    /// </summary>
    public string? Namespace { get; set; }
}
