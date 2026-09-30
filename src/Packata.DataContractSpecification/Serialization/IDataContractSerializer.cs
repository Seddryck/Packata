using Packata.Core.Storage;

namespace Packata.DataContractSpecification.Serialization;

public interface IDataContractSerializer
{
    DataContract Deserialize(StreamReader reader, IDocumentContainer container, IStorageProvider provider);
}
