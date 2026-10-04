using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class RoleSerializationTests
{
    [Test]
    public void Deserialize_Roles_PreservesV32Fields()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: roles
            version: 1.0.0
            status: active
            roles:
              - role: reader
              - id: writer-role
                role: writer
                access: write
                description: May update data
                firstLevelApprovers: Reporting Manager
                secondLevelApprovers: Data Owner
                customProperties:
                  - property: expires
                    value: annually
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Roles, Has.Count.EqualTo(2));
            Assert.That(contract.Roles[0].Role, Is.EqualTo("reader"));
            Assert.That(contract.Roles[0].Access, Is.Null);
            Assert.That(contract.Roles[1].Id, Is.EqualTo("writer-role"));
            Assert.That(contract.Roles[1].SecondLevelApprovers, Is.EqualTo("Data Owner"));
            Assert.That(contract.Roles[1].CustomProperties["expires"], Is.EqualTo("annually"));
        });
    }
}
