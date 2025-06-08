using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface IHostAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    [Label("Host")]
    public string Host { get; set; }

    /// <summary>
    /// The port to the database server.
    /// </summary>
    [Label("Port")]
    public int Port { get; set; }
}

