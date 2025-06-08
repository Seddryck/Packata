using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface IDatabaseAware
{
    /// <summary>
    /// The name of the database.
    /// </summary>
    [Label("Database")]
    public string Database { get; set; }
}

