using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class ServiceLevelAgreementSerializationTests
{
    [Test]
    public void Deserialize_ServiceLevelAgreements_PreservesV32Fields()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sla
            version: 1.0.0
            status: active
            slaDefaultElement: orders.created_at
            slaProperties:
              - property: availability
                value: 99.9
              - id: latency
                property: latency
                value: 4
                valueExt: 5
                unit: hours
                element: orders.created_at
                driver: operational
                scheduler: cron
                schedule: 0 6 * * *
                authoritativeDefinitions:
                  - url: https://example.com/sla
                    type: implementation
                customProperties:
                  - property: measurementWindow
                    value: monthly
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.SlaDefaultElement, Is.EqualTo("orders.created_at"));
            Assert.That(contract.SlaProperties, Has.Count.EqualTo(2));
            Assert.That(contract.SlaProperties[0].Property, Is.EqualTo("availability"));
            Assert.That(contract.SlaProperties[1].Scheduler, Is.EqualTo("cron"));
            Assert.That(contract.SlaProperties[1].Schedule, Is.EqualTo("0 6 * * *"));
            Assert.That(contract.SlaProperties[1].AuthoritativeDefinitions, Has.Count.EqualTo(1));
            Assert.That(contract.SlaProperties[1].CustomProperties["measurementWindow"], Is.EqualTo("monthly"));
        });
    }
}
