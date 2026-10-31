using Avro;
using Microsoft.Spark.Sql.Types;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;
namespace Packata.Application.Testing;
public class StructuredSchemaExportTests
{
    private static DataAsset Asset(params DataField[] fields) => new("orders", "orders", null, null, AssetKind.Table, new(fields));
    private static DataAsset Basic => Asset(new("id", "integer", Format: "i64", Required: true),
        new("name", "string", Nullable: true), new("amount", "number", Required: true, Constraints: [new("precision", 12), new("scale", 2)]));

    [Test]
    public void Spark_basic_example_matches_native_StructType_JSON()
    {
        var result = SchemaDocumentSerializer.Serialize(Basic, "spark-json");
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var expected = JObject.Parse("""{"type":"struct","fields":[{"name":"id","type":"long","nullable":false,"metadata":{}},{"name":"name","type":"string","nullable":true,"metadata":{}},{"name":"amount","type":"decimal(12,2)","nullable":false,"metadata":{}}]}""");
        Assert.That(JToken.DeepEquals(JObject.Parse(result.Content!), expected), Is.True);
        var parsed = (StructType)DataType.ParseDataType(result.Content!);
        Assert.That(parsed.Fields.Select(f => f.Name), Is.EqualTo(new[] { "id", "name", "amount" }));
        Assert.That(parsed.Fields[2].DataType, Is.InstanceOf<DecimalType>());
    }
    [Test]
    public void Avro_basic_example_parses_with_logical_decimal_and_no_inferred_defaults()
    {
        var result = SchemaDocumentSerializer.Serialize(Basic, "avro");
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(Schema.Parse(result.Content!), Is.InstanceOf<RecordSchema>());
        var json = JObject.Parse(result.Content!);
        Assert.That(json["fields"]![1]!["type"]![0]!.Value<string>(), Is.EqualTo("null"));
        Assert.That(json["fields"]![2]!["type"]!["logicalType"]!.Value<string>(), Is.EqualTo("decimal"));
        Assert.That(json["fields"]![2]!["type"]!["type"]!.Value<string>(), Is.EqualTo("bytes"));
        Assert.That(json["fields"]!.Any(f => f["default"] is not null), Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("SCHEMA_REQUIRED"));
    }
    [TestCase("spark-json")]
    [TestCase("avro")]
    public void Nested_arrays_maps_and_nullability_parse_and_preserve_order(string format)
    {
        var asset = Asset(new("customer", "object", Required: true, Children: [new("name", "string", Required: true), new("age", "integer", Nullable: true)]),
            new("tags", "array", Required: true, Items: new("tag", "string", Required: true, Nullable: true)),
            new("labels", "map", Required: true, Map: new(new("key", "string", Required: true), new("value", "integer", Nullable: true))));
        var first = SchemaDocumentSerializer.Serialize(asset, format);
        var second = SchemaDocumentSerializer.Serialize(asset, format);
        Assert.That(first.IsSuccessful, Is.True, string.Join(";", first.Diagnostics));
        Assert.That(first.Content, Is.EqualTo(second.Content));
        if (format == "avro") Assert.DoesNotThrow(() => Schema.Parse(first.Content!));
        else
        {
            var parsed = (StructType)DataType.ParseDataType(first.Content!);
            Assert.That(parsed.Fields[0].DataType, Is.InstanceOf<StructType>());
            Assert.That(parsed.Fields[1].DataType, Is.InstanceOf<ArrayType>());
            Assert.That(parsed.Fields[2].DataType, Is.InstanceOf<MapType>());
            Assert.That(JObject.Parse(first.Content!)["fields"]![1]!["type"]!["containsNull"]!.Value<bool>(), Is.True);
        }
    }
    [TestCase("spark-json")]
    [TestCase("avro")]
    public void Unsupported_or_lossy_domains_never_return_partial_content(string format)
    {
        var invalid = new DataField[] {
            new("value", "vector"), new("value", "integer", Format: "u64"),
            new("value", "number"), new("value", "number", Constraints: [new("precision", 2), new("scale", 3)]),
            new("value", "timestamp", Constraints: [new("precision", 9)]), new("value", "array"),
            new("value", "object"), new("value", "map"),
            new("value", "map", Map: new(new("key", "integer"), new("value", "string"))),
            new("value", "map", Map: new(new("key", "string", Nullable: true), new("value", "string"))) };
        foreach (var field in invalid)
        {
            var result = SchemaDocumentSerializer.Serialize(Asset(field), format);
            Assert.That(result.IsSuccessful, Is.False, field.ToString());
            Assert.That(result.Content, Is.Null, field.ToString());
            Assert.That(result.Diagnostics.Any(d => d.Severity == "Error"), Is.True);
        }
    }
    [Test]
    public void Avro_names_are_deterministic_and_collisions_fail_with_context()
    {
        var valid = SchemaDocumentSerializer.Serialize(Asset(new("a-b", "string"), new("quoted\"field", "string")), "avro", options: new("9 orders", "example.orders"));
        Assert.That(valid.IsSuccessful, Is.True, string.Join(";", valid.Diagnostics));
        Assert.DoesNotThrow(() => Schema.Parse(valid.Content!));
        Assert.That(JObject.Parse(valid.Content!)["name"]!.Value<string>(), Is.EqualTo("_9_orders"));
        var bad = SchemaDocumentSerializer.Serialize(Asset(new("a-b", "string"), new("a_b", "string")), "avro");
        Assert.That(bad.Content, Is.Null);
        Assert.That(bad.Diagnostics.Any(d => d.SourcePath.EndsWith("a_b") && d.Severity == "Error"), Is.True);
        var badNamespace = SchemaDocumentSerializer.Serialize(Basic, "avro", options: new(Namespace: "invalid-name"));
        Assert.That(badNamespace.Content, Is.Null);
    }
    [Test]
    public void Temporal_logical_types_preserve_instant_or_local_semantics_and_diagnose_offsets()
    {
        var asset = Asset(new("day", "date"), new("at", "timestamp", Constraints: [new("precision", 6), new("timezone", true)]));
        var avro = SchemaDocumentSerializer.Serialize(asset, "avro");
        Assert.DoesNotThrow(() => Schema.Parse(avro.Content!));
        Assert.That(avro.Content, Does.Contain("timestamp-micros").And.Contain("date"));
        Assert.That(avro.Diagnostics.Select(d => d.Code), Does.Contain("SCHEMA_TIMEZONE"));
        var spark = SchemaDocumentSerializer.Serialize(asset, "spark-json");
        Assert.DoesNotThrow(() => DataType.ParseDataType(spark.Content!));
        var local = SchemaDocumentSerializer.Serialize(Asset(new DataField("at", "timestamp", Constraints: [new("timezone", false)])), "avro");
        Assert.That(local.Content, Does.Contain("local-timestamp-micros"));
        Assert.DoesNotThrow(() => Schema.Parse(local.Content!));
    }
    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task ODCS_maps_preserve_explicit_key_value_shape(string format)
    {
        const string source = """
            apiVersion: v3.2.0
            kind: DataContract
            id: map-example
            schema:
              - name: orders
                properties:
                  - name: labels
                    logicalType: map
                    map:
                      key:
                        name: key
                        logicalType: string
                      value:
                        name: value
                        logicalType: integer
                        nullable: true
            """;
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Operation: "export", OutputFormat: format));
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        if (format == "avro") Assert.DoesNotThrow(() => Schema.Parse(result.Content!));
        else Assert.DoesNotThrow(() => DataType.ParseDataType(result.Content!));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task Equivalent_ODCS_and_DataPackage_assets_export_equivalent_documents(string format)
    {
        const string dp = """{"resources":[{"name":"orders","schema":{"fields":[{"name":"id","type":"integer","constraints":{"required":true}},{"name":"name","type":"string"}]}}]}""";
        const string odcs = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sales
            schema:
              - name: orders
                properties:
                  - name: id
                    logicalType: integer
                    required: true
                  - name: name
                    logicalType: string
                    nullable: true
            """;
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Operation: "export", OutputFormat: format);
        var left = await service.GenerateAsync(dp, options);
        var right = await service.GenerateAsync(odcs, options);
        Assert.That(left.IsSuccessful && right.IsSuccessful, Is.True, string.Join(";", left.Diagnostics.Concat(right.Diagnostics)));
        Assert.That(JToken.DeepEquals(JToken.Parse(left.Content!), JToken.Parse(right.Content!)), Is.True);
    }
}
