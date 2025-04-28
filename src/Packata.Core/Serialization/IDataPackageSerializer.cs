using Packata.Core.Storage;

namespace Packata.Core.Serialization;

public interface IDataPackageSerializer
{
    DataPackage Deserialize(StreamReader reader, IDataPackageContainer container, IStorageProvider provider);
}
