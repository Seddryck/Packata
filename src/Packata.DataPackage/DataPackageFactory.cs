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
    private readonly IStorageProvider _provider;
    private readonly ISerializerFactory _serializerFactory;

    public DataPackageFactory()
        : this(new SimpleDataPackageLocator(), new StorageProvider(), new SerializerFactory())
    { }

    public DataPackageFactory(IDataPackageLocator locator)
        : this(locator, new StorageProvider(), new SerializerFactory())
    { }

    public DataPackageFactory(IDataPackageLocator locator, IStorageProvider provider)
        : this(locator, provider, new SerializerFactory())
    { }

    protected internal DataPackageFactory(IDataPackageLocator locator, IStorageProvider provider, ISerializerFactory serializerFactory)
        => (_locator, _provider, _serializerFactory) = (locator, provider, serializerFactory);

    public DataPackage LoadFromStream(Stream stream, SerializationFormat format = SerializationFormat.Json)
        => LoadFromStream(stream, new LocalDirectoryDataPackageContainer(), format);

    protected DataPackage LoadFromStream(Stream stream, IDataPackageContainer container, string extension)
        => LoadFromStream(stream, container, _serializerFactory.Instantiate(extension));

    protected DataPackage LoadFromStream(Stream stream, IDataPackageContainer container, SerializationFormat format)
        => LoadFromStream(stream, container, _serializerFactory.Instantiate(format));

    protected DataPackage LoadFromStream(Stream stream, IDataPackageContainer container, IDataPackageSerializer serializer)
    {
        using var reader = new StreamReader(stream);
        var dataPackage = serializer.Deserialize(reader, container, _provider);
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
