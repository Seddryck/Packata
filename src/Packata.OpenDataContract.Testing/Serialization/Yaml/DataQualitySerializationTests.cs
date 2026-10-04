using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class DataQualitySerializationTests
{
    private static DataContract Deserialize(string quality)
    {
        var yaml = $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: quality
            version: 1.0.0
            status: active
            schema:
              - name: orders
                properties:
                  - name: email
                    logicalType: string
                    quality:
            {{quality}}
            """;
        var serializer = new DataContractSerializer();
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
    }

    [Test]
    public void Deserialize_TextQualityRule_PreservesCommonMetadata()
    {
        var contract = Deserialize("""
                          - id: email_verified
                            type: text
                            name: Verified email
                            description: The email was verified.
                            businessImpact: Invalid contact data
                            dimension: accuracy
                            severity: warning
                            tags: [email]
                            customProperties:
                              - property: owner
                                value: crm
            """);
        var rule = contract.Schema[0].Properties[0].Quality[0];

        Assert.Multiple(() =>
        {
            Assert.That(rule.Type, Is.EqualTo("text"));
            Assert.That(rule.Description, Is.EqualTo("The email was verified."));
            Assert.That(rule.Dimension, Is.EqualTo("accuracy"));
            Assert.That(rule.CustomProperties["owner"], Is.EqualTo("crm"));
        });
    }

    [Test]
    public void Deserialize_LibraryQualityRules_SupportsExplicitAndShorthandForms()
    {
        var contract = Deserialize("""
                          - id: missing-email
                            type: library
                            metric: missingValues
                            arguments:
                              missingValues: [null, '', N/A]
                            mustBeLessThan: 5
                            unit: percent
                          - id: invalid-email
                            metric: invalidValues
                            arguments:
                              pattern: '^[^@]+@[^@]+$'
                            mustBe: 0
            """);
        var rules = contract.Schema[0].Properties[0].Quality;

        Assert.Multiple(() =>
        {
            Assert.That(rules, Has.Count.EqualTo(2));
            Assert.That(rules[0].Type, Is.EqualTo("library"));
            Assert.That(rules[0].Metric, Is.EqualTo("missingValues"));
            Assert.That(rules[0].Arguments, Contains.Key("missingValues"));
            Assert.That(rules[0].MustBeLessThan, Is.Not.Null);
            Assert.That(rules[0].Unit, Is.EqualTo("percent"));
            Assert.That(rules[1].Type, Is.Null);
            Assert.That(rules[1].Metric, Is.EqualTo("invalidValues"));
            Assert.That(rules[1].MustBe, Is.Not.Null);
        });
    }
}
