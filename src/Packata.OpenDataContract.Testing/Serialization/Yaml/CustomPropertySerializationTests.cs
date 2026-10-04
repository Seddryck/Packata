using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class CustomPropertySerializationTests
{
    [Test]
    public void DeserializeAndSerialize_CustomProperties_PreservesMetadataAndDuplicates()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: custom-properties
            version: 1.0.0
            status: active
            customProperties:
              - id: first
                property: retention
                value: [one, two]
                description: First policy
                vendor: vendor-a
              - id: second
                property: retention
                value:
                  nested: true
                vendor: Unknown.Vendor
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        var serialized = serializer.Serialize(contract);

        Assert.Multiple(() =>
        {
            Assert.That(contract.CustomProperties, Has.Count.EqualTo(2));
            Assert.That(contract.CustomProperties[0].Id, Is.EqualTo("first"));
            Assert.That(contract.CustomProperties[0].Description, Is.EqualTo("First policy"));
            Assert.That(contract.CustomProperties[0].Vendor, Is.EqualTo("vendor-a"));
            Assert.That(contract.CustomProperties[1].Property, Is.EqualTo("retention"));
            Assert.That(contract.CustomProperties[1].Vendor, Is.EqualTo("Unknown.Vendor"));
            Assert.That(serialized, Does.Contain("vendor: Unknown.Vendor"));
            Assert.That(serialized, Does.Contain("description: First policy"));
        });
    }
}
