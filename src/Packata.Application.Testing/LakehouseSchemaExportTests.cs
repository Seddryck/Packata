using Avro;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public class LakehouseSchemaExportTests
{
    private static DataAsset Asset(params DataField[] fields) => new("orders", "orders", null, "Order records", AssetKind.Table, new(fields));
    private static ExtensionMetadata Metadata(string target, string key, object value) => ExtensionMetadata.For(target, new Dictionary<string, object?> { [key] = value });
    private static DataAsset Nested => Asset(
        new("id", "integer", Format: "i64", Required: true, Description: "Order identifier"),
        new("amount", "number", Required: true, Constraints: [new("precision", 12), new("scale", 2)]),
        new("at", "timestamp", Required: true, Constraints: [new("timezone", true), new("precision", 6)]),
        new("local", "timestamp", Nullable: true, Constraints: [new("timezone", false)]),
        new("day", "date", Required: true), new("clock", "time", Constraints: [new("precision", 6)]),
        new("customer", "object", Nullable: true, Children: [new("name", "string", Required: true)]),
        new("tags", "array", Required: true, Items: new("item", "object", Nullable: true, Children: [new("label", "string", Nullable: true)])),
        new("lookup", "map", Map: new(new("key", "integer", Required: true), new("value", "string", Nullable: true))));

    [TestCase("iceberg-json")]
    [TestCase("paimon-json")]
    public void Native_nested_schemas_are_deterministic_and_preserve_parameters(string format)
    {
        var result = SchemaDocumentSerializer.Serialize(Nested, format);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo(SchemaDocumentSerializer.Serialize(Nested, format).Content));
        var json = JObject.Parse(result.Content!);
        var fields = (JArray)json["fields"]!;
        Assert.That(fields.Select(f => f["name"]!.Value<string>()), Is.EqualTo(Nested.Schema!.Fields.Select(f => f.Name)));
        Assert.That(result.Content, Does.Contain("12, 2"));
        if (format == "iceberg-json")
        {
            Assert.That(json["schema-id"]!.Value<int>(), Is.Zero);
            Assert.That(fields[0]!["doc"]!.Value<string>(), Is.EqualTo("Order identifier"));
            Assert.That(fields[0]!["required"]!.Value<bool>(), Is.True);
            Assert.That(fields[2]!["type"]!.Value<string>(), Is.EqualTo("timestamptz"));
            Assert.That(fields[3]!["type"]!.Value<string>(), Is.EqualTo("timestamp"));
            Assert.That(fields[7]!["type"]!["element-required"]!.Value<bool>(), Is.False);
            Assert.That(fields[8]!["type"]!["value-required"]!.Value<bool>(), Is.False);
            var ids = json.SelectTokens("$..id").Concat(json.SelectTokens("$..['element-id']")).Concat(json.SelectTokens("$..['key-id']")).Concat(json.SelectTokens("$..['value-id']")).Select(t => t.Value<int>()).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
            Assert.That(ids.Order(), Is.EqualTo(Enumerable.Range(1, ids.Length)));
        }
        else
        {
            Assert.That(json["version"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(json["id"]!.Value<int>(), Is.Zero);
            Assert.That(fields[0]!["description"]!.Value<string>(), Is.EqualTo("Order identifier"));
            Assert.That(fields[0]!["type"]!.Value<string>(), Is.EqualTo("BIGINT NOT NULL"));
            Assert.That(fields[2]!["type"]!.Value<string>(), Is.EqualTo("TIMESTAMP_LTZ(6) NOT NULL"));
            Assert.That(fields[3]!["type"]!.Value<string>(), Is.EqualTo("TIMESTAMP(6)"));
            var ids = fields.SelectTokens("$..id").Select(t => t.Value<int>()).ToArray();
            Assert.That(ids.Order(), Is.EqualTo(Enumerable.Range(0, ids.Length)));
            Assert.That(json["highestFieldId"]!.Value<int>(), Is.EqualTo(ids.Max()));
            Assert.That(json["timeMillis"]!.Value<long>(), Is.Zero);
            Assert.That(json["options"], Is.Empty);
        }
    }

    [TestCase("iceberg-json", "iceberg")]
    [TestCase("paimon-json", "paimon")]
    public void Supplied_ids_are_reserved_before_allocating_and_schema_ids_are_independent(string format, string target)
    {
        var asset = Asset(new DataField("first", "string"), new("nested", "object", Children: [new("child", "integer", Extensions: Metadata(target, "field-id", 1))]));
        asset = asset with { Extensions = Metadata(target, "schema-id", 42) };
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var json = JObject.Parse(result.Content!);
        Assert.That(json[target == "iceberg" ? "schema-id" : "id"]!.Value<int>(), Is.EqualTo(42));
        Assert.That(json["fields"]![1]!["type"]!["fields"]![0]!["id"]!.Value<int>(), Is.EqualTo(1));
        Assert.That(json["fields"]![0]!["id"]!.Value<int>(), Is.Not.EqualTo(1));
    }

    [TestCase("iceberg-json", "iceberg", -1)]
    [TestCase("iceberg-json", "iceberg", 0)]
    [TestCase("iceberg-json", "iceberg", int.MaxValue)]
    [TestCase("paimon-json", "paimon", -1)]
    [TestCase("paimon-json", "paimon", int.MaxValue)]
    public void Invalid_or_reserved_ids_fail_with_field_context(string format, string target, int id)
    {
        var result = SchemaDocumentSerializer.Serialize(Asset(new DataField("bad", "string", Extensions: Metadata(target, "field-id", id))), format);
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Severity == "Error" && d.SourcePath == "orders/bad"), Is.True);
    }

    [TestCase("iceberg-json", "iceberg")]
    [TestCase("paimon-json", "paimon")]
    public void Duplicate_and_non_integer_ids_fail_without_content(string format, string target)
    {
        foreach (var id in new object[] { 7, "7.0", "invalid", 7.5, new[] { 7 } })
        {
            var result = SchemaDocumentSerializer.Serialize(Asset(new DataField("a", "string", Extensions: Metadata(target, "field-id", id)),
                new("b", "string", Extensions: Metadata(target, "field-id", id))), format);
            Assert.That(result.Content, Is.Null, id.ToString());
        }
    }

    [Test]
    public void Iceberg_identifiers_require_non_null_primitive_fields_and_never_enforce_uniqueness()
    {
        var asset = Asset(new DataField("id", "integer", Required: true)) with { Extensions = Metadata("iceberg", "identifier-fields", new[] { "id" }) };
        var result = SchemaDocumentSerializer.Serialize(asset, "iceberg-json");
        Assert.That(JObject.Parse(result.Content!)["identifier-field-ids"]!.Values<int>(), Is.EqualTo(new[] { 1 }));
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("SCHEMA_IDENTIFIER"));
        foreach (var field in new DataField[] { new("id", "integer", Nullable: true), new("id", "number", Format: "fp64", Required: true), new("id", "string") })
            Assert.That(SchemaDocumentSerializer.Serialize(asset with { Schema = new([field]) }, "iceberg-json").Content, Is.Null);
    }

    [Test]
    public void Paimon_keys_are_explicit_non_null_and_partitions_must_fit_primary_keys()
    {
        var asset = Asset(new DataField("id", "integer", Required: true), new("day", "date", Required: true));
        asset = asset with { Schema = asset.Schema! with { PrimaryKey = ["day", "id"] }, Extensions = Metadata("paimon", "partition-keys", new[] { "day" }) };
        var result = SchemaDocumentSerializer.Serialize(asset, "paimon-json");
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var json = JObject.Parse(result.Content!);
        Assert.That(json["primaryKeys"]!.Values<string>(), Is.EqualTo(new[] { "day", "id" }));
        Assert.That(json["partitionKeys"]!.Values<string>(), Is.EqualTo(new[] { "day" }));
        foreach (var keys in new string[][] { ["missing"], ["day"], ["id"], ["id", "id"] })
            Assert.That(SchemaDocumentSerializer.Serialize(asset with { Schema = asset.Schema with { PrimaryKey = keys } }, "paimon-json").Content, Is.Null);
        var nullable = asset with { Schema = asset.Schema with { Fields = [new("id", "integer", Required: true, Nullable: true), new("day", "date", Required: true)] } };
        Assert.That(SchemaDocumentSerializer.Serialize(nullable, "paimon-json").Content, Is.Null);
        Assert.That(SchemaDocumentSerializer.Serialize(asset with { Extensions = Metadata("paimon", "options", new Dictionary<string, string> { ["bucket"] = "4" }) }, "paimon-json").Content, Is.Null);
    }

    [TestCase("iceberg-json")]
    [TestCase("paimon-json")]
    public void Unsupported_shapes_constraints_and_parameters_are_diagnosed(string format)
    {
        foreach (var field in new DataField[] { new("value", "vector"), new("value", "integer", Format: "u64"), new("value", "number"),
            new("value", "number", Constraints: [new("precision", 39), new("scale", 2)]), new("value", "timestamp", Constraints: [new("precision", 9)]),
            new("value", "array"), new("value", "object"), new("value", "map"), new("value", "map", Map: new(new("key", "string", Nullable: true), new("value", "string"))) })
        {
            var result = SchemaDocumentSerializer.Serialize(Asset(field), format);
            Assert.That(result.Content, Is.Null, field.ToString());
            Assert.That(result.Diagnostics.Any(d => d.Severity == "Error" && d.SourcePath.Contains("value")), Is.True);
        }
        var constrained = SchemaDocumentSerializer.Serialize(Asset(new DataField("value", "string", Constraints: [new("pattern", "abc"), new("default", "abc")])), format);
        Assert.That(constrained.IsSuccessful, Is.True);
        Assert.That(constrained.Diagnostics.Select(d => d.Code), Does.Contain("SCHEMA_CONSTRAINT").And.Contain("SCHEMA_REQUIRED"));
        Assert.That(constrained.Content, Does.Not.Contain("defaultValue"));
        Assert.That(SchemaDocumentSerializer.Serialize(Asset(new DataField("a", "string"), new("a", "integer")), format).Content, Is.Null);
    }

    [TestCase("hudi", "avro")]
    [TestCase("delta", "spark-json")]
    public void Profiles_reuse_generic_serializers_and_never_infer_table_settings(string profile, string format)
    {
        var asset = Asset(new DataField("id", "integer", Required: true), new("text", "string", Nullable: true), new("at", "timestamp", Constraints: [new("timezone", true)]));
        var generic = SchemaDocumentSerializer.Serialize(asset, format);
        var result = SchemaDocumentSerializer.Serialize(asset, format, profile: profile);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo(generic.Content));
        if (profile == "hudi") Assert.DoesNotThrow(() => Schema.Parse(result.Content!));
        Assert.That(result.Content, Does.Not.Contain("_hoodie_").And.Not.Contain("delta.columnMapping").And.Not.Contain("default"));
    }

    [Test]
    public void Hudi_Spark_subset_rejects_generic_Avro_domains_with_profile_context()
    {
        foreach (var field in new DataField[] { new("clock", "time"), new("local", "timestamp", Constraints: [new("timezone", false)]),
            new("amount", "number", Constraints: [new("precision", 39), new("scale", 2)]), new("name-with-dash", "string"),
            new("_hoodie_record_key", "string"), new("value", "string", Constraints: [new("enum", new[] { "A", "B" })]), new("value", "string", Constraints: [new("default", "A")]) })
        {
            Assert.That(SchemaDocumentSerializer.Serialize(Asset(field), "avro").IsSuccessful, Is.True, field.ToString());
            var result = SchemaDocumentSerializer.Serialize(Asset(field), "avro", profile: "hudi");
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SCHEMA_PROFILE" && d.Target == "hudi" && d.SourcePath.EndsWith(field.Name)), Is.True);
        }
    }

    [Test]
    public void Delta_validates_nested_case_collisions_names_and_reserved_metadata()
    {
        foreach (var field in new DataField[] { new("a b", "string"), new("nested", "object", Children: [new("ID", "integer"), new("id", "string")]),
            new("array", "array", Items: new("element", "object", Children: [new("x", "string"), new("X", "string")])),
            new("mapped", "string", Extensions: Metadata("delta", "columnMapping.id", 12)),
            new("generated", "integer", Extensions: Metadata("delta", "generationExpression", "1")) })
        {
            Assert.That(SchemaDocumentSerializer.Serialize(Asset(field), "spark-json").IsSuccessful, Is.True);
            var result = SchemaDocumentSerializer.Serialize(Asset(field), "spark-json", profile: "delta");
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SCHEMA_PROFILE" && d.Target == "delta"), Is.True);
        }
    }

    [TestCase("iceberg-json", null)]
    [TestCase("paimon-json", null)]
    [TestCase("avro", "hudi")]
    [TestCase("spark-json", "delta")]
    public async Task Both_standards_and_both_application_hosts_share_export_and_cancellation(string format, string? profile)
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
        var options = new GenerationOptions(Operation: "export", OutputFormat: format, CompatibilityProfile: profile);
        var left = await new SchemaGenerationService().GenerateAsync(dp, options);
        var right = await new SchemaGenerationService().GenerateAsync(odcs, options);
        Assert.That(left.IsSuccessful && right.IsSuccessful, Is.True, string.Join(";", left.Diagnostics.Concat(right.Diagnostics)));
        var l = JToken.Parse(left.Content!); var r = JToken.Parse(right.Content!);
        foreach (var json in new[] { l, r }) foreach (var metadata in json.SelectTokens("$..packata").ToArray()) ((JProperty)metadata.Parent!).Remove();
        Assert.That(JToken.DeepEquals(l, r), Is.True);
        var host = await SchemaDocumentService.ExportAsync(ContractDocument.Parse(dp), new(format, Profile: profile));
        Assert.That(host.Content, Is.EqualTo(left.Content));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        _ = Assert.ThrowsAsync<OperationCanceledException>(async () => await new SchemaGenerationService().GenerateAsync(dp, options, cancellationToken: cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => SchemaDocumentSerializer.Serialize(Nested, format, cancelled.Token, profile: profile));
    }

    [Test]
    public void Catalog_exposes_profile_baselines_and_rejects_wrong_operation_and_formats()
    {
        Assert.That(SchemaGenerationService.Catalog.CompatibilityProfiles.Select(p => p.Id), Is.EquivalentTo(new[] { "hudi", "delta" }));
        foreach (var profile in SchemaGenerationService.Catalog.CompatibilityProfiles)
        {
            Assert.That(profile.Baseline, Is.Not.Empty);
            Assert.That(profile.DiagnosticCapabilities, Is.Not.Empty);
            Assert.That(SchemaGenerationService.Catalog.Targets.Single(t => t.Id == profile.OutputFormat).CompatibilityProfiles, Does.Contain(profile.Id));
        }
        foreach (var options in new GenerationOptions[] { new(Operation: "export", OutputFormat: "iceberg-json", CompatibilityProfile: "hudi"),
            new(Operation: "export", OutputFormat: "paimon-json", CompatibilityProfile: "delta"), new(Operation: "export", OutputFormat: "avro", CompatibilityProfile: "unknown"),
            new(Operation: "export", OutputFormat: "avro", CompatibilityProfile: "delta"), new(Operation: "export", OutputFormat: "spark-json", CompatibilityProfile: "hudi"),
            new(Target: "record", Language: "csharp", CompatibilityProfile: "hudi"), new(Operation: "export", OutputFormat: "iceberg-json", Language: "json") })
            Assert.That(SchemaGenerationService.ValidateOptions(options, null), Is.Not.Null, options.ToString());
    }

    [Test]
    public void Iceberg_reserves_supplied_collection_ids_and_Paimon_rejects_non_field_ids()
    {
        var asset = Asset(new DataField("items", "array", Items: new("item", "string", Nullable: true, Extensions: Metadata("iceberg", "field-id", 1))));
        var result = SchemaDocumentSerializer.Serialize(asset, "iceberg-json");
        var type = JObject.Parse(result.Content!)["fields"]![0]!["type"]!;
        Assert.That(type["element-id"]!.Value<int>(), Is.EqualTo(1));
        Assert.That(JObject.Parse(result.Content!)["fields"]![0]!["id"]!.Value<int>(), Is.EqualTo(2));
        var paimon = Asset(new DataField("items", "array", Items: new("item", "string", Extensions: Metadata("paimon", "field-id", 1))));
        Assert.That(SchemaDocumentSerializer.Serialize(paimon, "paimon-json").Content, Is.Null);
    }

    [TestCase("iceberg-json", "iceberg")]
    [TestCase("paimon-json", "paimon")]
    public async Task ODCS_custom_property_IDs_remain_explicit_integer_declarations(string format, string target)
    {
        var source = $$"""
            apiVersion: v3.2.0
            kind: DataContract
            id: example
            schema:
              - name: orders
                customProperties:
                  - property: packata.{{target}}.schema-id
                    value: 7
                properties:
                  - name: id
                    logicalType: integer
                    customProperties:
                      - property: packata.{{target}}.field-id
                        value: 42
            """;
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Operation: "export", OutputFormat: format));
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var root = JObject.Parse(result.Content!);
        Assert.That(root[target == "iceberg" ? "schema-id" : "id"]!.Value<int>(), Is.EqualTo(7));
        Assert.That(root["fields"]![0]!["id"]!.Value<int>(), Is.EqualTo(42));
    }

    [Test]
    public void Delta_rejects_reserved_asset_metadata_without_changing_generic_Spark()
    {
        var asset = Asset(new DataField("id", "integer")) with { Extensions = Metadata("delta", "columnMapping.mode", "name") };
        Assert.That(SchemaDocumentSerializer.Serialize(asset, "spark-json").IsSuccessful, Is.True);
        Assert.That(SchemaDocumentSerializer.Serialize(asset, "spark-json", profile: "delta").Content, Is.Null);
    }
}
