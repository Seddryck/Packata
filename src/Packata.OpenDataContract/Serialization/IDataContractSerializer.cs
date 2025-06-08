using Packata.Core.Storage;

namespace Packata.OpenDataContract.Serialization;

public interface IDataContractSerializer
{
    DataContract Deserialize(StreamReader reader, IDataPackageContainer container, IStorageProvider provider);
}
