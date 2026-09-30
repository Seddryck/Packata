using Packata.Core.Storage;

namespace Packata.DataPackage.Serialization;

public interface IDataPackageSerializer
{
    DataPackage Deserialize(StreamReader reader, IDocumentContainer container, IStorageProvider provider);
}
