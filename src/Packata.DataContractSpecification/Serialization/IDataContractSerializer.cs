using Packata.Core.Storage;

namespace Packata.DataContractSpecification.Serialization;

public interface IDataContractSerializer
{
    DataContract Deserialize(StreamReader reader, IDataPackageContainer container, IStorageProvider provider);
}
