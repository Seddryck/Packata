using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface ISchemaAware
{
    /// <summary>
    /// The name of the schema in the database.
    /// </summary>
    [Label("Schema")]
    public string Schema { get; set; }
}

