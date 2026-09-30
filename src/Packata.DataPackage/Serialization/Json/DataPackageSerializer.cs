using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.Storage;

namespace Packata.Core.Serialization.Json;
internal class DataPackageSerializer : IDataPackageSerializer
{
    public DataPackage Deserialize(StreamReader reader, IDataPackageContainer container, IStorageProvider provider)
    {
        var resolver = new DataPackagePropertyResolver(container, provider);
        var serializer = new JsonSerializer
        {
            ContractResolver = resolver
        };
        var dataPackage = serializer.Deserialize<DataPackage>(new JsonTextReader(reader))
                            ?? throw new JsonSerializationException("The JSON data is not valid.");
        return dataPackage;
    }
}
