using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;
using Packata.Core.Serialization.Yaml;

namespace Packata.Core;

public class DataPackageFactory
{
    public DataPackage LoadFromStream(Stream stream, SerializationFormat? format = SerializationFormat.Json)
        => format switch
        {
            SerializationFormat.Json => LoadJsonFromStream(stream),
            SerializationFormat.Yaml => LoadYamlFromStream(stream),
            _ => throw new NotSupportedException("The specified serialization format is not supported.")
        };

    protected DataPackage LoadJsonFromStream(Stream stream)
    {
        var resolver = new DataPackagePropertyResolver(new HttpClient(), GetType().Assembly.Location);
        var serializer = new JsonSerializer
        {
            ContractResolver = resolver
        };
        var dataPackage = serializer.Deserialize<DataPackage>(new JsonTextReader(new StreamReader(stream)))
                            ?? throw new JsonSerializationException("The JSON data is not valid.");
        return dataPackage;
    }

    protected DataPackage LoadYamlFromStream(Stream stream)
    {
        var factory = new YamlSerializerFactory();
        var serializer = factory.CreateDeserializer(new HttpClient(), GetType().Assembly.Location);
        var dataPackage = serializer.Deserialize<DataPackage>(new StreamReader(stream))
                            ?? throw new JsonSerializationException("The YAML data is not valid.");
        return dataPackage;
    }

    public DataPackage LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The specified file does not exist.", path);

        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public enum SerializationFormat
    {
        Json,
        Yaml
    }
}
