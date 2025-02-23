using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Packata.Core.Serialization.Json;
internal class DataPackageSerializer : IDataPackageSerializer
{
    public DataPackage Deserialize(StreamReader reader, HttpClient httpClient, string root)
    {
        var resolver = new DataPackagePropertyResolver(httpClient, root);
        var serializer = new JsonSerializer
        {
            ContractResolver = resolver
        };
        var dataPackage = serializer.Deserialize<DataPackage>(new JsonTextReader(reader))
                            ?? throw new JsonSerializationException("The JSON data is not valid.");
        return dataPackage;
    }
}
