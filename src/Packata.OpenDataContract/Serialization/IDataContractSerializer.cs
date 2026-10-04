using Packata.Core.Storage;

namespace Packata.OpenDataContract.Serialization;

public interface IDataContractSerializer
{
    DataContract Deserialize(StreamReader reader, IDocumentContainer container, IStorageProvider provider);

    string Serialize(DataContract dataContract);
}
