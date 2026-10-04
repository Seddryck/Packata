using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;
using Packata.OpenDataContract.ServerTypes;
using YamlDotNet.Core;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class V32ValidationTests
{
    private static IEnumerable<TestCaseData> InvalidProperties()
    {
        yield return Invalid("""
            name: status
            logicalType: string
            enum: []
            """, "at least one value");
        yield return Invalid("""
            name: status
            logicalType: string
            enum:
              - label: Missing
            """, "required value");
        yield return Invalid("""
            name: status
            logicalType: string
            enum:
              - value: active
              - value: active
            """, "duplicate enum value");
        yield return Invalid("""
            name: quantity
            logicalType: integer
            enum:
              - value: many
            """, "incompatible");
        yield return Invalid("""
            name: embedding
            logicalType: vector
            """, "positive integer");
        yield return Invalid("""
            name: embedding
            logicalType: vector
            logicalTypeOptions:
              dimensions: 0
            """, "positive integer");
        yield return Invalid("""
            name: amount
            logicalType: number
            semanticType: metric
            """, "semanticType");
        yield return Invalid("""
            name: status
            logicalType: string
            synonyms:
              - description: Missing name
            """, "requires a non-empty synonym");
    }

    [TestCaseSource(nameof(InvalidProperties))]
    public void Deserialize_InvalidPropertyConstraint_IdentifiesProperty(string propertyYaml, string expectedDiagnostic)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize(ContractWithProperty(propertyYaml)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("schema/orders/properties"));
            Assert.That(exception.Message, Does.Contain(expectedDiagnostic));
        });
    }

    [Test]
    public void Deserialize_OmittedDefaultsAndExplicitNestedDeprecation_AppliesConsistently()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: defaults
            version: 1.0.0
            status: active
            schema:
              - name: orders
                properties:
                  - name: address
                    logicalType: object
                    properties:
                      - name: old_postcode
                        logicalType: string
                        deprecated: true
            servers:
              - server: local
                type: local
                path: data.csv
                format: csv
            """;

        var contract = Deserialize(yaml);
        var outer = contract.Schema[0].Properties[0];
        var nested = outer.Properties[0];

        Assert.Multiple(() =>
        {
            Assert.That(contract.Schema[0].Deprecated, Is.False);
            Assert.That(outer.Deprecated, Is.False);
            Assert.That(outer.SemanticType, Is.EqualTo("column"));
            Assert.That(nested.Deprecated, Is.True);
            Assert.That(nested.SemanticType, Is.EqualTo("column"));
            Assert.That(((LocalFilesServer)contract.Servers[0]).Encoding, Is.EqualTo("UTF-8"));
        });
    }

    [Test]
    public void Deserialize_NonstandardEncodingOnSupportedServer_PreservesValue()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: encoding
            version: 1.0.0
            status: active
            servers:
              - server: archive
                type: s3
                location: s3://archive
                format: csv
                encoding: x-enterprise-ebcdic
            """;

        var contract = Deserialize(yaml);

        Assert.That(((S3Server)contract.Servers[0]).Encoding, Is.EqualTo("x-enterprise-ebcdic"));
    }

    [Test]
    public void Deserialize_EncodingOnUnsupportedServer_RejectsItsServerPath()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: encoding
            version: 1.0.0
            status: active
            servers:
              - server: warehouse
                type: sqlserver
                host: database
                encoding: UTF-8
            """;

        var exception = Assert.Throws<YamlException>(() => Deserialize(yaml));

        Assert.That(exception!.Message, Does.Contain("$/servers[0]").And.Contain("does not permit encoding"));
    }

    [Test]
    public void Deserialize_SynonymsOutsideSchema_RejectsTheirLocation()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: synonyms
            version: 1.0.0
            status: active
            synonyms:
              - synonym: invalid
            """;

        var exception = Assert.Throws<YamlException>(() => Deserialize(yaml));

        Assert.That(exception!.Message, Does.Contain("$").And.Contain("only permitted"));
    }

    private static TestCaseData Invalid(string propertyYaml, string expectedDiagnostic)
        => new(propertyYaml, expectedDiagnostic);

    private static string ContractWithProperty(string propertyYaml)
    {
        var lines = propertyYaml.Split('\n');
        var indented = string.Join(Environment.NewLine,
            new[] { $"      - {lines[0].TrimEnd('\r')}" }
                .Concat(lines.Skip(1).Select(line => $"        {line.TrimEnd('\r')}")));
        return $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: invalid
            version: 1.0.0
            status: active
            schema:
              - name: orders
                properties:
            {{indented}}
            """;
    }

    private static DataContract Deserialize(string yaml)
    {
        var serializer = new DataContractSerializer();
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
    }
}
