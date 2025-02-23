using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Serialization;
internal class SerializerFactory
{
    public IDataPackageSerializer Instantiate(string format)
        => format.ToLowerInvariant() switch
        {
            "json" => new Json.DataPackageSerializer(),
            "yaml" or "yml" => new Yaml.DataPackageSerializer(),
            _ => throw new ArgumentOutOfRangeException(),
        };
}
