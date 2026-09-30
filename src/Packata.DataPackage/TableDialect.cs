using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents the table dialect as defined by the Data Package Table Dialect profile.
/// </summary>
public abstract class TableDialect
{
    /// <summary>
    /// The profile of this descriptor.
    /// </summary>
    public string Profile { get; set; } = "https://datapackage.org/profiles/1.0/tabledialect.json";

    /// <summary>
    /// The type of the dialect. Can be "delimited", "structured", "spreadsheet" or "database"
    /// </summary>
    public string? Type { get; set; }
}
