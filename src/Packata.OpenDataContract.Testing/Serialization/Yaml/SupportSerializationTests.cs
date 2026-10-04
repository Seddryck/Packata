using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class SupportSerializationTests
{
    [Test]
    public void Deserialize_SupportChannels_PreservesV32Fields()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: support
            version: 1.0.0
            status: active
            support:
              - channel: '#help'
              - id: notifications
                channel: releases
                description: Release notifications
                invitationUrl: https://example.com/invite
                scope: notifications
                tool: googlechat
                customProperties:
                  - property: audience
                    value: everyone
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Support, Has.Count.EqualTo(2));
            Assert.That(contract.Support[0].Channel, Is.EqualTo("#help"));
            Assert.That(contract.Support[0].Url, Is.Null);
            Assert.That(contract.Support[1].Id, Is.EqualTo("notifications"));
            Assert.That(contract.Support[1].Scope, Is.EqualTo("notifications"));
            Assert.That(contract.Support[1].Tool, Is.EqualTo("googlechat"));
            Assert.That(contract.Support[1].CustomProperties["audience"], Is.EqualTo("everyone"));
        });
    }
}
