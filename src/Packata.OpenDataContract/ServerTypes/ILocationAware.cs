using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface ILocationAware
{
    /// <summary>
    /// The location of the server.
    /// </summary>
    [Label("Location")]
    public string Location { get; set; }
}

