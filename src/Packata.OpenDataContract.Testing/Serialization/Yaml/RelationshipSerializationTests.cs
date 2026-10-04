using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;
using YamlDotNet.Core;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class RelationshipSerializationTests
{
    private static DataContract Deserialize(string relationships)
    {
        var yaml = $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: relationships
            version: 1.0.0
            status: active
            schema:
              - name: orders
                relationships:
            {{relationships}}
                properties:
                  - name: customer_id
                    logicalType: string
                    relationships:
                      - id: property:customer
                        to: schema/customers/properties/id
            """;
        var serializer = new DataContractSerializer();
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
    }

    [Test]
    public void Deserialize_RelationshipIdentifiers_SupportsBothLevelsAndNamespacedIds()
    {
        var contract = Deserialize("""
                  - id: source:customer
                    from: orders.customer_id
                    to: customers.id
            """);

        Assert.Multiple(() =>
        {
            Assert.That(contract.Schema[0].Relationships[0].Id, Is.EqualTo("source:customer"));
            Assert.That(contract.Schema[0].Properties[0].Relationships[0].Id, Is.EqualTo("property:customer"));
        });
    }

    [Test]
    public void Deserialize_DuplicateRelationshipIdentifiers_Throws()
    {
        Assert.That(() => Deserialize("""
                  - id: duplicate
                    from: orders.customer_id
                    to: customers.id
                  - id: duplicate
                    from: orders.account_id
                    to: accounts.id
            """), Throws.TypeOf<YamlException>().With.Message.Contains("duplicated"));
    }

    [TestCase(".")]
    [TestCase("#")]
    [TestCase("/")]
    [TestCase("\\")]
    [TestCase("@")]
    [TestCase("!")]
    [TestCase("%")]
    [TestCase("&")]
    [TestCase("^")]
    [TestCase(" ")]
    public void Deserialize_ProhibitedRelationshipIdentifierCharacter_Throws(string character)
    {
        Assert.That(() => Deserialize($$"""
                  - id: 'invalid{{character}}id'
                    from: orders.customer_id
                    to: customers.id
            """), Throws.TypeOf<YamlException>().With.Message.Contains("prohibited"));
    }
}
