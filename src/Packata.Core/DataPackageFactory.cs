using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;

namespace Packata.Core;

public class DataPackageFactory
{
    public DataPackage LoadFromStream(Stream stream)
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

    public DataPackage LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The specified file does not exist.", path);

        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }
}
