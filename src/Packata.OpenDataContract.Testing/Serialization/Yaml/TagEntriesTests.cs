using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Storage;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.Serialization.Yaml;
using YamlDotNet.Core;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public sealed class TagEntriesTests
{
    private const string Contract = """
        apiVersion: v3.2.0
        kind: DataContract
        id: example
        version: 1.0.0
        status: draft
        """;

    [Test]
    public void MixedTags_RoundTripAcrossAllSharedLocations()
    {
        const string tags = """[finance, {pii: true}, "001", {text: "true"}, {days: 365}, {ratio: 1.25}]""";
        var yaml = Contract + "\n" + $$"""
            tags: {{tags}}
            team:
              tags: {{tags}}
              members:
                - username: owner@example.com
                  tags: {{tags}}
            schema:
              - name: customers
                tags: {{tags}}
                quality:
                  - type: text
                    description: Example
                    tags: {{tags}}
                properties:
                  - name: status
                    logicalType: string
                    tags: {{tags}}
                    enum:
                      - value: active
                        tags: {{tags}}
            """;
        var serializer = new DataContractSerializer();
        var original = Read(serializer, yaml);

        AssertTags(original);
        AssertTags(Read(serializer, serializer.Serialize(original)));

        original.Tags[0] = "sales";
        var updated = Read(serializer, serializer.Serialize(original));
        Assert.That(updated.TagEntries.Take(3), Is.EqualTo(new object[] { "sales", new KeyValueTag("pii", true), "001" }));
    }

    [Test]
    public void TagEntries_ReadAnchoredLabelsAndMappings()
    {
        var contract = Read(new DataContractSerializer(), Contract + "\ntags: [&label finance, *label, &tag {pii: &enabled true}, *tag, {audit: *enabled}]\nname: *label");
        Assert.Multiple(() =>
        {
            Assert.That(contract.Name, Is.EqualTo("finance"));
            Assert.That(contract.TagEntries, Is.EqualTo(new object[]
            {
                "finance", "finance", new KeyValueTag("pii", true), new KeyValueTag("pii", true), new KeyValueTag("audit", true)
            }));
        });
    }

    [Test]
    public void TagEntries_ReadExplicitIntegersAndTreatKeysAsMetadata()
    {
        var native = Read(new DataContractSerializer(), Contract + "\ntags: [{encoding: !!int '0xFF'}, {synonyms: true}]");
        Assert.That(native.TagEntries, Is.EqualTo(new object[]
        {
            new KeyValueTag("encoding", 255L), new KeyValueTag("synonyms", true)
        }));
    }

    [Test]
    public void Mapping_KeepsTypedTagMetadataAtEachLocation()
    {
        var native = Read(new DataContractSerializer(), Contract + "\n" + """
            tags: [finance, {pii: true}]
            team:
              members:
                - username: owner@example.com
                  tags: [{scope: account}]
            schema:
              - name: customers
                tags: [{days: 365}]
                quality:
                  - type: text
                    description: Example
                    tags: [{level: high}]
                properties:
                  - name: status
                    logicalType: string
                    tags: [{critical: false}]
                    enum:
                      - value: active
                        tags: [{managed: "true"}]
            """);
        var contract = new OpenDataContractMapper().Map(native).Value!;
        var asset = contract.Assets.Single();
        var field = asset.Schema!.Fields.Single();
        var team = (IReadOnlyDictionary<string, object?>)contract.Extensions["odcs"]["team"]!;
        var member = ((IEnumerable<object>)team["members"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
        var option = field.ValueDomain!.Entries!.Single();

        native.TagEntries = new([new KeyValueTag("pii", false)]);

        Assert.Multiple(() =>
        {
            Assert.That(contract.Metadata.Tags, Is.EqualTo(new[] { "finance" }));
            Assert.That(KeyedValue(contract.Extensions["odcs"]["tags"], "pii"), Is.EqualTo(true));
            Assert.That(KeyedValue(asset.Extensions["odcs"]["tags"], "days"), Is.EqualTo(365L));
            Assert.That(KeyedValue(field.Extensions["odcs"]["tags"], "critical"), Is.EqualTo(false));
            Assert.That(KeyedValue(option.Extensions["odcs"]["tags"], "managed"), Is.EqualTo("true"));
            Assert.That(KeyedValue(asset.QualityRules.Single().Extensions["odcs"]["tags"], "level"), Is.EqualTo("high"));
            Assert.That(KeyedValue(member["tags"], "scope"), Is.EqualTo("account"));
        });
    }

    [TestCase("[{}]")]
    [TestCase("[{pii: true, owner: sales}]")]
    [TestCase("""[{"": true}]""")]
    [TestCase("[{pii: null}]")]
    [TestCase("[{pii: {nested: true}}]")]
    [TestCase("[{pii: [true]}]")]
    [TestCase("[{pii: !!bool 1}]")]
    [TestCase("[{limit: 0x8000000000000000}]")]
    [TestCase("[{limit: 0x10000000000000000}]")]
    [TestCase("[{limit: 0o8}]")]
    [TestCase("[true]")]
    public void NativeReader_RejectsInvalidTags(string tags)
        => Assert.That(() => Read(new DataContractSerializer(), Contract + "\ntags: " + tags), Throws.InstanceOf<YamlException>());

    private static DataContract Read(DataContractSerializer serializer, string yaml)
    {
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        return serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
    }

    private static object? KeyedValue(object? tags, string key)
        => ((IEnumerable<object>)tags!).OfType<IReadOnlyDictionary<string, object?>>().Single()[key];

    private static void AssertTags(DataContract contract)
    {
        var asset = contract.Schema.Single();
        var field = asset.Properties.Single();
        var expected = new object[]
        {
            "finance", new KeyValueTag("pii", true), "001", new KeyValueTag("text", "true"),
            new KeyValueTag("days", 365L), new KeyValueTag("ratio", 1.25m)
        };
        Assert.Multiple(() =>
        {
            Assert.That(contract.Tags, Is.EqualTo(new[] { "finance", "001" }));
            Assert.That(contract.TagEntries, Is.EqualTo(expected));
            Assert.That(asset.TagEntries, Is.EqualTo(expected));
            Assert.That(field.TagEntries, Is.EqualTo(expected));
            Assert.That(field.Enum!.Single().TagEntries, Is.EqualTo(expected));
            Assert.That(asset.Quality.Single().TagEntries, Is.EqualTo(expected));
            Assert.That(contract.Team!.TagEntries, Is.EqualTo(expected));
            Assert.That(contract.Team.Members.Single().TagEntries, Is.EqualTo(expected));
        });
    }
}
