using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Packata.Core.Serialization;
using Packata.Core.Serialization.Json;
using Packata.Core.Storage;

namespace Packata.Core;

public class DataPackageFactory
{
    private readonly IDataPackageLocator _locator;
    private readonly ISerializerFactory _serializerFactory;

    public DataPackageFactory()
        : this(new SimpleDataPackageLocator(), new SerializerFactory())
    { }

    public DataPackageFactory(IDataPackageLocator locator)
        : this(locator, new SerializerFactory())
    { }

    protected internal DataPackageFactory(IDataPackageLocator locator, ISerializerFactory serializerFactory)
        => (_locator, _serializerFactory) = (locator, serializerFactory);

    public DataPackage LoadFromStream(Stream stream, SerializationFormat format = SerializationFormat.Json)
        => LoadFromStream(stream, new LocalDirectoryDataPackageContainer(), format);

    protected DataPackage LoadFromStream(Stream stream, IDataPackageContainer container, string extension)
    {
        var serializer = _serializerFactory.Instantiate(extension);
        using var reader = new StreamReader(stream);
        var dataPackage = serializer.Deserialize(reader, container);
        return dataPackage;
    }

    protected DataPackage LoadFromStream(Stream stream, IDataPackageContainer container, SerializationFormat format)
    {
        var serializer = _serializerFactory.Instantiate(format);
        using var reader = new StreamReader(stream);
        var dataPackage = serializer.Deserialize(reader, container);
        return dataPackage;
    }

    public DataPackage LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The specified file does not exist.", path);
        using var stream = File.OpenRead(path);
        var fileInfo = new FileInfo(path);
        return LoadFromStream(stream, new LocalDirectoryDataPackageContainer(new Uri(fileInfo.Directory!.FullName)), Path.GetExtension(path));
    }

    public async Task<DataPackage> LoadFromContainer(Uri containerUri, string descriptorPath = "datapackage.json")
    {
        var handle = await _locator.LocateAsync(containerUri, descriptorPath);
        await handle.ValidateAsync();
        var stream = handle.Container.OpenAsync(handle.DescriptorPath);
        return LoadFromStream(await stream, handle.Container, Path.GetExtension(handle.DescriptorPath));
    }
}
