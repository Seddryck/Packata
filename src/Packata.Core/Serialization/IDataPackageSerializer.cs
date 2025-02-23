namespace Packata.Core.Serialization;

public interface IDataPackageSerializer
{
    DataPackage Deserialize(StreamReader reader, HttpClient httpClient, string root);
}
