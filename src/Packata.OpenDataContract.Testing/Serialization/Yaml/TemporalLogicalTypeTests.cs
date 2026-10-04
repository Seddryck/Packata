using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;
using Packata.OpenDataContract.Types;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class TemporalLogicalTypeTests
{
    [Test]
    public void Deserialize_TimestampAndTime_ReturnsDedicatedLogicalTypes()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: temporal
            version: 1.0.0
            status: active
            schema:
              - name: events
                properties:
                  - name: created_at
                    logicalType: timestamp
                    logicalTypeOptions:
                      format: yyyy-MM-ddTHH:mm:ssZ
                  - name: starts_at
                    logicalType: time
                    logicalTypeOptions:
                      format: HH:mm:ss
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Schema[0].Properties[0].LogicalType, Is.TypeOf<TimestampLogicalType>());
            Assert.That(((TimestampLogicalType)contract.Schema[0].Properties[0].LogicalType!).Format, Is.EqualTo("yyyy-MM-ddTHH:mm:ssZ"));
            Assert.That(contract.Schema[0].Properties[1].LogicalType, Is.TypeOf<TimeLogicalType>());
            Assert.That(((TimeLogicalType)contract.Schema[0].Properties[1].LogicalType!).Format, Is.EqualTo("HH:mm:ss"));
        });
    }
}
