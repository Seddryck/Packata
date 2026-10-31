using Avro;
using Avro.Generic;
using Avro.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;
namespace Packata.Application.Testing;

public class AvroEnumExportTests
{
    private static DataAsset Asset(params DataField[] fields) => new("members", "members", null, null, AssetKind.Table, new(fields));
    private static DataField Status(bool nullable = false) => new("status", "string", Required: true, Nullable: nullable,
        Constraints: [new("enum", new[] { "GOLD", "SILVER" })]);

    [TestCase(false)]
    [TestCase(true)]
    public void Compatible_values_become_native_enums_without_inferred_defaults(bool nullable)
    {
        var result = SchemaDocumentSerializer.Serialize(Asset(Status(nullable)), "avro");
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var parsed = (RecordSchema)Schema.Parse(result.Content!);
        var type = parsed.Fields[0].Schema;
        if (nullable)
        {
            var union = (UnionSchema)type;
            Assert.That(union.Schemas[0].Tag, Is.EqualTo(Schema.Type.Null));
            type = union.Schemas[1];
        }
        var enumeration = (EnumSchema)type;
        Assert.That(enumeration.Symbols, Is.EqualTo(new[] { "GOLD", "SILVER" }));
        Assert.That(JObject.Parse(result.Content!)["fields"]![0]!["default"], Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "SCHEMA_CONSTRAINT"), Is.False);
        var writer = new GenericDatumWriter<object>(enumeration);
        using var stream = new MemoryStream();
        writer.Write(new GenericEnum(enumeration, "GOLD"), new BinaryEncoder(stream));
        Assert.That(() => writer.Write(new GenericEnum(enumeration, "INVALID"), new BinaryEncoder(stream)), Throws.Exception);
    }

    [Test]
    public void Value_domains_use_restricted_values_and_never_category_labels()
    {
        var gold = ScalarValue.From("GOLD"); var silver = ScalarValue.From("SILVER");
        var field = new DataField("status", "string", ValueDomain: new([new(gold, "Gold member"), new(silver, "Silver member")], [silver]));
        var result = SchemaDocumentSerializer.Serialize(Asset(field), "avro");
        var type = (EnumSchema)((RecordSchema)Schema.Parse(result.Content!)).Fields[0].Schema;
        Assert.That(type.Symbols, Is.EqualTo(new[] { "SILVER" }));
        var unresolved = field with { ValueDomain = new(reference: new("external-codes")) };
        var unknown = SchemaDocumentSerializer.Serialize(Asset(unresolved), "avro");
        Assert.That(((RecordSchema)Schema.Parse(unknown.Content!)).Fields[0].Schema.Tag, Is.EqualTo(Schema.Type.String));
    }

    [Test]
    public void Nested_fields_array_items_and_map_values_get_unique_deterministic_enum_names()
    {
        var asset = Asset(new("nested", "object", Children: [Status()]),
            new("states", "array", Items: Status(true)),
            new("codes", "map", Map: new(new("key", "string"), Status())),
            new("nested_status_enum", "object", Children: [new("id", "integer")]));
        var first = SchemaDocumentSerializer.Serialize(asset, "avro");
        var second = SchemaDocumentSerializer.Serialize(asset, "avro");
        Assert.That(first.IsSuccessful, Is.True, string.Join(";", first.Diagnostics));
        Assert.That(first.Content, Is.EqualTo(second.Content));
        Assert.DoesNotThrow(() => Schema.Parse(first.Content!));
        var json = JObject.Parse(first.Content!);
        Assert.That(json["fields"]![1]!["type"]!["items"]![1]!["type"]!.Value<string>(), Is.EqualTo("enum"));
        Assert.That(json["fields"]![2]!["type"]!["values"]!["type"]!.Value<string>(), Is.EqualTo("enum"));
    }

    public static IEnumerable<object[]> Incompatible => new object[][] {
        [new[] { "GOLD member", "SILVER" }], [new[] { "9GOLD", "SILVER" }],
        [new object[] { 1, 2 }], [new object[] { "GOLD", 2 }], [new[] { "GOLD", "GOLD" }],
        [Array.Empty<string>()], [new[] { "GOLD\n" }] };

    [TestCaseSource(nameof(Incompatible))]
    public void Incompatible_sets_keep_the_original_string_representation(object values)
    {
        var result = SchemaDocumentSerializer.Serialize(Asset(new DataField("status", "string", Constraints: [new("enum", values)])), "avro");
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(((RecordSchema)Schema.Parse(result.Content!)).Fields[0].Schema.Tag, Is.EqualTo(Schema.Type.String));
        Assert.That(result.Diagnostics.Any(d => d.Code is "SCHEMA_ENUM_FORMAT_LIMIT" or "SCHEMA_IMPLEMENTATION_GAP"), Is.True);
    }
}
