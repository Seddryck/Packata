using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Serialization;
internal class SerializerFactory : ISerializerFactory
{
    public IDataPackageSerializer Instantiate(string extension)
        => Instantiate(extension.ToLowerInvariant().TrimStart('.') switch
        {
            "json" => SerializationFormat.Json,
            "yaml" or "yml" => SerializationFormat.Yaml,
            _ => throw new ArgumentOutOfRangeException(
                    nameof(extension),
                    extension,
                    "Unsupported extension. Supported extensions are: json, yaml, yml")
        });

    public IDataPackageSerializer Instantiate(SerializationFormat format)
        => format switch
        {
            SerializationFormat.Json => new Json.DataPackageSerializer(),
            SerializationFormat.Yaml => new Yaml.DataPackageSerializer(),
            _ => throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Unsupported format. Supported formats are: json, yaml")
        };
}
