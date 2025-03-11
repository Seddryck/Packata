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
    public DataPackage LoadFromStream(Stream stream, string format = "json", string? root = null)
    {
        var serializer = new DataPackageSerializer();
        var dataPackage = serializer.Deserialize(new StreamReader(stream), new HttpClient(), root ?? GetType().Assembly.Location);
        return dataPackage;
    }

    public DataPackage LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The specified file does not exist.", path);

        var extension = Path.GetExtension(path) switch
        {
            ".json" => "json",
            _ => throw new NotSupportedException("The specified file format is not supported.")
        };

        var root = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar.ToString();

        using var stream = File.OpenRead(path);
        return LoadFromStream(stream, extension, root);
    }
}
