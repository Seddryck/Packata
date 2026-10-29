using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class DataQualitySerializationTests
{
    [Test]
    public void QualityOperandsAndArgumentsPreserveYamlTypesThroughRoundTrip()
    {
        var contract = Deserialize("""
                          - type: library
                            metric: missingValues
                            mustBe: 2
                            arguments:
                              missingValues: [null, 1, '1', false, 'false']
                          - type: sql
                            query: SELECT true
                            mustBe: true
                          - type: sql
                            query: SELECT '2'
                            mustBe: '2'
                          - type: library
                            metric: rowCount
                            mustBeBetween: [1, 3.5]
            """);
        var serialized = new DataContractSerializer().Serialize(contract);
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(serialized)));
        var roundtrip = new DataContractSerializer().Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        var rules = roundtrip.Schema[0].Properties[0].Quality;
        Assert.That(rules[0].MustBe, Is.EqualTo(2L));
        Assert.That((System.Collections.IEnumerable)rules[0].Arguments["missingValues"], Is.EqualTo(new object?[] { null, 1L, "1", false, "false" }));
        Assert.That(rules[1].MustBe, Is.EqualTo(true));
        Assert.That(rules[2].MustBe, Is.EqualTo("2"));
        Assert.That(rules[3].MustBeBetween, Is.EqualTo(new object[] { 1L, 3.5m }));
    }

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
    public void Serialize_PreservesExplicitNullAndAbsentOperands()
    {
        var contract = Deserialize("""
                          - metric: rowCount
                            mustBe: null
                          - metric: rowCount
            """);
        var yaml = new DataContractSerializer().Serialize(contract);
        Assert.That(yaml.Split("mustBe:").Length - 1, Is.EqualTo(1));
        Assert.That(yaml, Does.Not.Contain("mustBeBetween:"));
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var roundtrip = new DataContractSerializer().Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        Assert.That(roundtrip.Schema[0].Properties[0].Quality[0].DeclaredExpectations.ContainsKey("MustBe"), Is.True);
        Assert.That(roundtrip.Schema[0].Properties[0].Quality[1].DeclaredExpectations, Is.Empty);
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

    [Test]
    public void Deserialize_SqlQualityRule_PreservesQueryAndPlaceholders()
    {
        var contract = Deserialize("""
                          - id: sql-email
                            type: sql
                            query: |
                              SELECT COUNT(*)
                              FROM {object}
                              WHERE {property} IS NULL
                            mustBe: 0
            """);
        var rule = contract.Schema[0].Properties[0].Quality[0];

        Assert.Multiple(() =>
        {
            Assert.That(rule.Type, Is.EqualTo("sql"));
            Assert.That(rule.Query, Does.Contain("FROM {object}"));
            Assert.That(rule.Query, Does.Contain("{property} IS NULL"));
            Assert.That(rule.MustBe, Is.Not.Null);
        });
    }

    [Test]
    public void Deserialize_CustomQualityRule_PreservesVendorImplementation()
    {
        var contract = Deserialize("""
                          - id: soda-duplicates
                            type: custom
                            engine: soda
                            implementation: |
                              type: duplicate_percent
                              columns:
                                - email
            """);
        var rule = contract.Schema[0].Properties[0].Quality[0];

        Assert.Multiple(() =>
        {
            Assert.That(rule.Type, Is.EqualTo("custom"));
            Assert.That(rule.Engine, Is.EqualTo("soda"));
            Assert.That(rule.Implementation, Does.Contain("duplicate_percent"));
            Assert.That(rule.Implementation, Does.Contain("- email"));
        });
    }

    [Test]
    public void Deserialize_QualityScheduling_PreservesSchedulerAndExpression()
    {
        var contract = Deserialize("""
                          - id: scheduled-email-check
                            type: sql
                            query: SELECT COUNT(*) FROM {object}
                            scheduler: airflow
                            schedule: 0 20 * * *
            """);
        var rule = contract.Schema[0].Properties[0].Quality[0];

        Assert.Multiple(() =>
        {
            Assert.That(rule.Scheduler, Is.EqualTo("airflow"));
            Assert.That(rule.Schedule, Is.EqualTo("0 20 * * *"));
        });
    }
}
