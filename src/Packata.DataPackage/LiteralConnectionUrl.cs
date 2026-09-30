using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataPackage;
public class LiteralConnectionUrl : IConnection
{
    public string ConnectionUrl { get; }

    public LiteralConnectionUrl(string connectionUrl)
        => ConnectionUrl = connectionUrl;
}
