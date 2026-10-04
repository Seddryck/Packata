using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class TeamSerializationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Deserialize_Team_SupportsCanonicalAndDeprecatedShapes(bool deprecated)
    {
        var team = deprecated
            ? """
                team:
                  - username: legacy@example.com
                    role: Owner
                """
            : """
                team:
                  id: maintainers
                  name: Maintainers
                  description: Contract maintainers
                  members:
                    - id: owner
                      username: owner@example.com
                      role: Owner
                      tags: [primary]
                      customProperties:
                        - property: timezone
                          value: Europe/Brussels
                """;
        var yaml = $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: team
            version: 1.0.0
            status: active
            {{team}}
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Team, Is.Not.Null);
            Assert.That(contract.Team!.Members, Has.Count.EqualTo(1));
            Assert.That(contract.Team.UsesDeprecatedArrayStructure, Is.EqualTo(deprecated));
            Assert.That(contract.Team.Members[0].Username, Does.EndWith("@example.com"));
        });
    }
}
