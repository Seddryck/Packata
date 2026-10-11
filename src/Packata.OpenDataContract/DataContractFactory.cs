using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract;

/// <summary>Loads Open Data Contract Standard documents.</summary>
public sealed class DataContractFactory
{
    private readonly IDataContractSerializer _serializer = new DataContractSerializer();
    private readonly IStorageProvider _provider = new StorageProvider();

    public DataContract LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("The specified file does not exist.", path);

        using var stream = File.OpenRead(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return LoadFromStream(stream, new FactoryDocumentContainer(directory));
    }

    public DataContract LoadFromStream(Stream stream)
        => LoadFromStream(stream, new FactoryDocumentContainer(Directory.GetCurrentDirectory()));

    private DataContract LoadFromStream(Stream stream, IDocumentContainer container)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        return _serializer.Deserialize(reader, container, _provider);
    }

    private sealed class FactoryDocumentContainer(string directory) : IDocumentContainer
    {
        public Uri BaseUri { get; } = new(Path.GetFullPath(directory) + Path.DirectorySeparatorChar);

        public Task<Stream> OpenAsync(string relativePath) =>
            Task.FromResult<Stream>(File.OpenRead(Path.Combine(directory, relativePath)));

        public Task<bool> ExistsAsync(string relativePath) =>
            Task.FromResult(File.Exists(Path.Combine(directory, relativePath)));

        public void Dispose() { }
    }
}
