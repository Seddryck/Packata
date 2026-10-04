using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class ContextSerializationTests
{
    [Test]
    public void DeserializeAndSerialize_Context_SupportsBothLevelsAndStringShorthand()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: context
            version: 1.0.0
            status: active
            context: Use ${REGION} revenue data only.
            schema:
              - name: turnover
                context:
                  instructions: Always filter by date.
                  verifiedStatements:
                    - id: revenue
                      question: What was total revenue?
                      answer: Sum the revenue measure.
                      customProperties:
                        - property: confidence
                          value: curated
                  constraints:
                    - id: no-pii
                      constraint: Do not expose individual records.
                      tags: [pii]
                      authoritativeDefinitions:
                        - url: https://example.com/ontology
                          type: ontology
                          description: Privacy ontology
                properties: []
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        var serialized = serializer.Serialize(contract);

        Assert.Multiple(() =>
        {
            Assert.That(contract.Context!.Instructions, Is.EqualTo("Use ${REGION} revenue data only."));
            Assert.That(contract.Context.UsesStringShorthand, Is.True);
            Assert.That(contract.Schema[0].Context!.VerifiedStatements[0].Question, Is.EqualTo("What was total revenue?"));
            Assert.That(contract.Schema[0].Context!.Constraints[0].Constraint, Does.StartWith("Do not"));
            Assert.That(contract.Schema[0].Context!.Constraints[0].AuthoritativeDefinitions[0].Type, Is.EqualTo("ontology"));
            Assert.That(serialized, Does.Contain("Use ${REGION} revenue data only."));
        });
    }
}
