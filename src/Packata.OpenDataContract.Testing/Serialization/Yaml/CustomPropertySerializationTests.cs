using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Storage;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class CustomPropertySerializationTests
{
    [Test]
    public void Column_default_round_trips_and_only_recognizes_the_packata_vendor()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: defaults
            version: 1.0.0
            status: active
            schema:
              - name: sample
                logicalType: object
                properties:
                  - name: status
                    logicalType: string
                    customProperties:
                      - vendor: other
                        property: defaultValue
                        value: anything
                      - property: defaultValue
                        value: metadata
                      - vendor: packata
                        property: defaultValue
                        value: {type: string, value: "O'Brien"}
            """;
        var serializer = new DataContractSerializer();
        DataContract Load(string input)
        {
            using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)));
            return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        }

        var roundTrip = Load(serializer.Serialize(Load(yaml)));
        var field = roundTrip.Schema.Single().Properties.Single();
        Assert.That(field.CustomProperties, Has.Count.EqualTo(3));
        var canonical = roundTrip.ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        Assert.That(canonical.DefaultValue?.Value, Is.EqualTo("O'Brien"));
    }

    [TestCase("{kind: current-timestamp}", ColumnDefaultKind.CurrentTimestamp, null)]
    [TestCase("{kind: current-utc-timestamp, fractionalSecondsPrecision: 0}", ColumnDefaultKind.CurrentUtcTimestamp, 0)]
    [TestCase("{kind: current-date}", ColumnDefaultKind.CurrentDate, null)]
    [TestCase("{kind: current-time, fractionalSecondsPrecision: 9}", ColumnDefaultKind.CurrentTime, 9)]
    [TestCase("{kind: next-sequence-value, sequence: {schema: Billing Schema, name: Invoice Numbers}}",
        ColumnDefaultKind.NextSequenceValue, null)]
    public void Column_default_expression_round_trips(string declaration, ColumnDefaultKind kind, int? precision)
    {
        var yaml = $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: defaults
            version: 1.0.0
            status: active
            schema:
              - name: sample
                logicalType: object
                properties:
                  - name: value
                    logicalType: string
                    customProperties:
                      - vendor: packata
                        property: defaultExpression
                        value: {{declaration}}
            """;
        var serializer = new DataContractSerializer();
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var document = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        using var roundTripReader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(serializer.Serialize(document))));
        var roundTrip = serializer.Deserialize(roundTripReader, Mock.Of<IDocumentContainer>(), new StorageProvider());
        var field = roundTrip.ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        Assert.That(field.DefaultExpression, Is.EqualTo(new ColumnDefaultExpression(kind, precision,
            kind == ColumnDefaultKind.NextSequenceValue ? new("Invoice Numbers", Schema: "Billing Schema") : null)));
    }

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
