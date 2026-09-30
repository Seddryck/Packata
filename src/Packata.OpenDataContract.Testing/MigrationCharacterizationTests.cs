using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using OdcsSerializer = Packata.OpenDataContract.Serialization.Yaml.DataContractSerializer;

namespace Packata.OpenDataContract.Testing;

public class MigrationCharacterizationTests
{
    [Test]
    public void ExistingFormats_CanBeDeserializedSideBySide()
    {
        const string dataPackageJson = """
            {
              "profile": "https://datapackage.org/profiles/2.0/datapackage.json",
              "name": "orders",
              "resources": [
                {
                  "name": "orders",
                  "type": "table",
                  "format": "csv",
                  "path": "orders.csv",
                  "schema": { "fields": [{ "name": "id", "type": "integer" }] }
                }
              ]
            }
            """;

        const string odcsYaml = """
            apiVersion: v3.0.2
            kind: DataContract
            id: orders
            name: orders
            version: 1.0.0
            status: active
            schema:
              - name: orders
                physicalType: table
                properties:
                  - name: id
                    logicalType: integer
            """;

        var container = Mock.Of<IDataPackageContainer>();
        var provider = Mock.Of<IStorageProvider>();

        using var packageStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dataPackageJson));
        using var contractReader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(odcsYaml)));

        var package = new Packata.Core.DataPackageFactory().LoadFromStream(packageStream);
        var contract = new OdcsSerializer().Deserialize(contractReader, container, provider);

        Assert.Multiple(() =>
        {
            Assert.That(package.Name, Is.EqualTo("orders"));
            Assert.That(package.Resources, Has.Count.EqualTo(1));
            Assert.That(package.Resources[0].Schema!.Fields[0].Name, Is.EqualTo("id"));
            Assert.That(contract.Name, Is.EqualTo("orders"));
            Assert.That(contract.Schema, Has.Count.EqualTo(1));
            Assert.That(contract.Schema[0].Properties[0].Name, Is.EqualTo("id"));
        });
    }
}
