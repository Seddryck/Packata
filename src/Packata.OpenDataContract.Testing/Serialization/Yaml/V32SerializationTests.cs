using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;
using Packata.OpenDataContract.ServerTypes;
using Packata.OpenDataContract.Types;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class V32SerializationTests
{
    [Test]
    public void Deserialize_SchemaExtensions_PreservesV32Metadata()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: v32
            version: 1.0.0
            status: active
            schema:
              - name: products
                synonyms:
                  - synonym: inventory
                    locale: en
                properties:
                  - name: attributes
                    logicalType: map
                    semanticType: dimension
                    deprecated: true
                    map:
                      key:
                        name: key
                        logicalType: string
                      value:
                        name: value
                        logicalType: string
                  - name: embedding
                    logicalType: vector
                    logicalTypeOptions:
                      dimensions: 1536
                      elementType: float32
                      distanceMetric: cosine
                      normalized: true
                      embeddingModel: text-embedding
                      embeddingModelVersion: v2
                  - name: status
                    logicalType: string
                    enum:
                      - value: active
                        label: Active
                        description: Available for use
                        tags: [current]
            """;

        var contract = Deserialize(yaml);
        var properties = contract.Schema[0].Properties;

        Assert.Multiple(() =>
        {
            Assert.That(contract.Schema[0].Synonyms[0].SynonymValue, Is.EqualTo("inventory"));
            Assert.That(properties[0].LogicalType, Is.TypeOf<MapLogicalType>());
            Assert.That(properties[0].Map!.Key.LogicalType, Is.TypeOf<StringLogicalType>());
            Assert.That(properties[0].SemanticType, Is.EqualTo("dimension"));
            Assert.That(properties[0].Deprecated, Is.True);
            Assert.That(properties[1].LogicalType, Is.TypeOf<VectorLogicalType>());
            Assert.That(((VectorLogicalType)properties[1].LogicalType!).Dimensions, Is.EqualTo(1536));
            Assert.That(((VectorLogicalType)properties[1].LogicalType!).DistanceMetric, Is.EqualTo("cosine"));
            Assert.That(properties[2].Enum![0].Label, Is.EqualTo("Active"));
        });
    }

    [Test]
    public void Deserialize_NewServerTypesAndAliases_UsesDedicatedModels()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: servers
            version: 1.0.0
            status: active
            servers:
              - server: query
                type: athena
                workgroup: analytics
                stagingDir: s3://bucket/results
              - { server: hana, type: hana, host: db }
              - { server: iceberg, type: iceberg, catalog: prod, catalogUrl: https://catalog }
              - { server: exasol, type: exasol, host: db }
              - { server: teradata, type: teradata, host: db }
              - { server: ingres, type: ingres, host: db, database: sales }
              - { server: vectorwise, type: vectorwise, host: db, database: sales }
              - { server: versant, type: versant, database: sales }
              - { server: poet, type: poet, database: sales }
              - { server: old-poet, type: fastobjects, database: sales }
              - { server: zen, type: zen, host: db }
              - { server: old-zen, type: btrieve, host: db }
            """;

        var servers = Deserialize(yaml).Servers;

        Assert.Multiple(() =>
        {
            Assert.That(servers[0], Is.TypeOf<AthenaServer>());
            Assert.That(((AthenaServer)servers[0]).Workgroup, Is.EqualTo("analytics"));
            Assert.That(((AthenaServer)servers[0]).StagingDir, Is.EqualTo("s3://bucket/results"));
            Assert.That(servers[1], Is.TypeOf<HanaServer>());
            Assert.That(servers[2], Is.TypeOf<IcebergServer>());
            Assert.That(servers[3], Is.TypeOf<ExasolServer>());
            Assert.That(servers[4], Is.TypeOf<TeradataServer>());
            Assert.That(servers[5], Is.TypeOf<IngresServer>());
            Assert.That(servers[6], Is.TypeOf<VectorwiseServer>());
            Assert.That(servers[7], Is.TypeOf<VersantServer>());
            Assert.That(servers[8], Is.TypeOf<PoetServer>());
            Assert.That(servers[9], Is.TypeOf<PoetServer>());
            Assert.That(servers[10], Is.TypeOf<ZenServer>());
            Assert.That(servers[11], Is.TypeOf<ZenServer>());
        });
    }

    [Test]
    public void Deserialize_EncodingAndVariablePort_PreservesValuesDuringRoundTrip()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: variables
            version: 1.0.0
            status: active
            servers:
              - server: events
                type: kafka
                host: ${KAFKA_HOST:-localhost}
                port: ${KAFKA_PORT:-9092}
                format: json
                encoding: UTF-16
            """;

        var serializer = new DataContractSerializer();
        var contract = Deserialize(yaml, serializer);
        var server = (KafkaServer)contract.Servers[0];
        var serialized = serializer.Serialize(contract);

        Assert.Multiple(() =>
        {
            Assert.That(server.Host, Is.EqualTo("${KAFKA_HOST:-localhost}"));
            Assert.That(server.Port, Is.EqualTo("${KAFKA_PORT:-9092}"));
            Assert.That(server.Encoding, Is.EqualTo("UTF-16"));
            Assert.That(serialized, Does.Contain("${KAFKA_HOST:-localhost}"));
            Assert.That(serialized, Does.Contain("${KAFKA_PORT:-9092}"));
        });
    }

    [Test]
    public void Resolve_VariableExpressions_UsesValuesAndDefaultsExplicitly()
    {
        var values = new Dictionary<string, string?> { ["HOST"] = "database.example" };

        Assert.That(
            OdcsVariableResolver.Resolve("tcp://${HOST}:${PORT:-5432}", values),
            Is.EqualTo("tcp://database.example:5432"));
    }

    private static DataContract Deserialize(string yaml, DataContractSerializer? serializer = null)
    {
        serializer ??= new DataContractSerializer();
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
    }
}
