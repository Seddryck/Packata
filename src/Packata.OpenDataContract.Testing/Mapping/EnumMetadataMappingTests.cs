using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class EnumMetadataMappingTests
{
    [Test]
    public void Native_yaml_round_trip_retains_numeric_looking_strings_and_integral_numbers()
    {
        var source = new DataContract
        {
            Id = "sample",
            Version = "1.0.0",
            Status = "draft",
            Schema = [new SchemaObject
        { Name="data", Properties=[new SchemaProperty {Name="value",Enum=[new() {Value="001",Label="First"},new() {Value=1m},new() {Value=true}]}] }]
        };
        var yaml = new Packata.OpenDataContract.Serialization.Yaml.DataContractSerializer().Serialize(source);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var restored = new DataContractFactory().LoadFromStream(stream);
        var before = source.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0].ValueDomain!.Entries!;
        var after = restored.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0].ValueDomain!.Entries!;
        Assert.That(after.Select(option => (option.Value, option.Label)), Is.EqualTo(before.Select(option => (option.Value, option.Label))), yaml);
    }
    [Test]
    public void Native_mapping_snapshots_values_labels_descriptions_and_additional_metadata()
    {
        var nested = new Dictionary<string, object?> { ["text"] = "original" };
        var entry = new EnumerationValue
        {
            Value = "001",
            Label = "First",
            Description = "Handle immediately",
            Id = "first",
            Tags = ["active"],
            CustomProperties = [new() { Property = "translation", Value = nested }],
            AuthoritativeDefinitions = [new() { Url = "https://example.com/first", Type = "glossary", Description = "Original" }]
        };
        var property = new SchemaProperty { Name = "code", Enum = [entry, new() { Value = "B", Description = "" }] };
        var mapped = Map(property);
        var entries = Entries(mapped);
        Assert.That(mapped.Constraints.Single().Value, Is.EqualTo(new[] { "001", "B" }));
        Assert.That(entries.Select(x => x["value"]), Is.EqualTo(new[] { "001", "B" }));
        Assert.That(entries[0]["label"], Is.EqualTo("First"));
        Assert.That(entries[0]["description"], Is.EqualTo("Handle immediately"));
        Assert.That(entries[1]["label"], Is.Null);
        Assert.That(entries[1]["description"], Is.EqualTo(""));
        var before = System.Text.Json.JsonSerializer.Serialize(entries);
        entry.Value = "changed"; entry.Label = "Changed"; entry.Description = "Changed"; entry.Tags.Add("changed");
        entry.AuthoritativeDefinitions[0].Description = "Changed"; nested["text"] = "changed";
        property.Enum.Clear();
        Assert.That(System.Text.Json.JsonSerializer.Serialize(entries), Is.EqualTo(before));
        Assert.That(mapped.Constraints.Single().Value, Is.EqualTo(new[] { "001", "B" }));
    }

    [Test]
    public void Yaml_mapping_preserves_enum_metadata_for_nested_and_array_item_properties()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sample
            version: 1.0.0
            status: draft
            schema:
              - name: records
                properties:
                  - name: code
                    logicalType: string
                    enum:
                      - value: '001'
                        label: First
                        description: String code
                      - value: B
                  - name: details
                    logicalType: object
                    properties:
                      - name: priority
                        logicalType: integer
                        enum:
                          - value: 1
                            label: High
                            description: Handle immediately
                  - name: codes
                    logicalType: array
                    items:
                      name: code
                      logicalType: string
                      enum:
                        - value: A
                          description: Item code
                  - name: unconstrained
                    logicalType: string
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var document = new DataContractFactory().LoadFromStream(stream);
        var fields = document.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields;
        Assert.That(Entries(fields[0])[0]["value"], Is.TypeOf<string>().And.EqualTo("001"));
        Assert.That(Entries(fields[0])[1]["label"], Is.Null);
        Assert.That(Entries(fields[0])[1]["description"], Is.Null);
        Assert.That(Entries(fields[1].Children[0])[0]["description"], Is.EqualTo("Handle immediately"));
        Assert.That(Entries(fields[2].Items!)[0]["description"], Is.EqualTo("Item code"));
        Assert.That(fields[1].Children[0].ValueDomain!.Entries!.Single().Value.Value, Is.TypeOf<long>().And.EqualTo(1L));
        Assert.That(fields[1].Children[0].ValueDomain!.Entries!.Single().Description, Is.EqualTo("Handle immediately"));
        Assert.That(fields[2].Items!.ValueDomain!.Entries!.Single().Description, Is.EqualTo("Item code"));
        Assert.That(fields[3].Extensions.TryGetNamespace("odcs", out _), Is.False);
    }

    private static DataField Map(SchemaProperty property) => new OpenDataContractMapper().Map(new DataContract
    { Id = "sample", Schema = [new SchemaObject { Name = "data", Properties = [property] }] }).RequireValue().Assets[0].Schema!.Fields[0];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries(DataField field)
        => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)field.Extensions["odcs"]["enum"]!;
}
