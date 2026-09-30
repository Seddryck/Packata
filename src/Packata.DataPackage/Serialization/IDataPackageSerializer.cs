using Packata.Core.Storage;

namespace Packata.Core.Serialization;

public interface IDataPackageSerializer
{
    DataPackage Deserialize(StreamReader reader, IDocumentContainer container, IStorageProvider provider);
}
