using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class ApiServer : BaseServer, ILocationAware
{
    /// <summary>
    /// URL to the API
    /// </summary>
    [Label("Location")]
    public required string Location { get; set; }
}
