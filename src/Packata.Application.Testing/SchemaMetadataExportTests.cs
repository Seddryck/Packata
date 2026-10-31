using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avro;
using Microsoft.Spark.Sql.Types;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public class SchemaMetadataExportTests
{
    private static DataAsset Members(params DataField[] fields) => new("members", "members", null, null, AssetKind.Table,
        new(fields, ["member_id"], [new(["category_code"], "categories", ["code"], "member_category")], AllowAdditionalProperties: false, EnforcePropertyOrder: true));

    private static JObject FieldMetadata(JToken field, string format) => format == "avro"
        ? (JObject)field["packata"]!["field"]!
        : JObject.Parse(field["metadata"]!["packata"]!["field"]!.Value<string>()!);

    private static JObject SchemaMetadata(JToken schema, string format) => format == "avro"
        ? (JObject)schema["packata"]!["schema"]!
        : JObject.Parse(schema["fields"]![0]!["metadata"]!["packata"]!["schema"]!.Value<string>()!);

    private static JToken RoundTrip(string content, string format)
    {
        if (format == "avro") return JToken.Parse(Schema.Parse(content).ToString());
        return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? JToken.Parse(SparkRoundTrip(content)) : JToken.Parse(content);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string SparkRoundTrip(string content) => DataType.ParseDataType(content).Json;

    private static DataAsset Plain(params DataField[] fields) => new("members", "members", null, null, AssetKind.Table, new(fields));

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Native_types_and_presence_do_not_repeat_names_nullability_or_exact_numeric_parameters(string format)
    {
        var asset = Plain(
            new("id", "integer", Format: "i64", Required: true, Nullable: true,
                Constraints: [new("integerWidth", 64), new("signed", true), new("required", true)]),
            new("amount", "number", Required: true, Constraints: [new("precision", 12), new("scale", 2)]),
            new("ratio", "number", Format: "fp32", Required: true));
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        Assert.That(result.IsSuccessful, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
        var schema = RoundTrip(result.Content!, format);
        if (format == "avro")
        {
            Assert.That(schema.SelectTokens("$..packata"), Is.Empty, "Every declaration is already represented natively.");
            Assert.That(schema["fields"]![0]!["type"]![0]!.Value<string>(), Is.EqualTo("null"));
            Assert.That(schema["fields"]![1]!["type"]!["precision"]!.Value<int>(), Is.EqualTo(12));
        }
        else
        {
            Assert.That(schema.SelectTokens("$..packata.field"), Is.Empty);
            Assert.That(SchemaMetadata(schema, format).Properties().Select(p => p.Name), Is.EqualTo(new[] { "assetId" }));
            Assert.That(schema["fields"]![0]!["nullable"]!.Value<bool>(), Is.True);
            Assert.That(schema["fields"]![1]!["type"]!.Value<string>(), Is.EqualTo("decimal(12,2)"));
        }
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Sparse_metadata_preserves_only_lost_ranges_optional_presence_and_annotations(string format)
    {
        var asset = Plain(new DataField("small id", "integer", Format: "u8", Description: "Identifier",
            Constraints: [new("integerWidth", 8), new("signed", false)]));
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        var schema = RoundTrip(result.Content!, format);
        var field = FieldMetadata(schema["fields"]![0]!, format);
        Assert.That(field["required"]!.Value<bool>(), Is.False);
        Assert.That(field["nullable"], Is.Null);
        Assert.That(field["physicalType"], Is.Null);
        Assert.That(field["valueDomain"], Is.Null);
        Assert.That(field["children"], Is.Null);
        Assert.That(field["qualityRules"], Is.Null);
        Assert.That(field["format"]!.Value<string>(), Is.EqualTo("u8"));
        Assert.That(field["constraints"], Is.Null, "The residual u8 format already retains width and signedness.");
        if (format == "avro")
        {
            Assert.That(field["name"]!.Value<string>(), Is.EqualTo("small id"));
            Assert.That(field["description"], Is.Null);
            Assert.That(schema["fields"]![0]!["doc"]!.Value<string>(), Is.EqualTo("Identifier"));
        }
        else
        {
            Assert.That(field["name"], Is.Null);
            Assert.That(field["description"]!.Value<string>(), Is.EqualTo("Identifier"));
        }
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Membership_is_stored_once_and_domain_details_are_retained(string format)
    {
        var values = new[] { ScalarValue.From("GOLD"), ScalarValue.From("SILVER") };
        var domain = new ValueDomain([new(values[0], "Gold", "Loyalty"), new(values[1])], values, ordered: false);
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("category", "string", Required: true, ValueDomain: domain)), format);
        var schema = RoundTrip(result.Content!, format);
        var metadata = FieldMetadata(schema["fields"]![0]!, format);
        var details = metadata["valueDomain"]!;
        Assert.That(details["allowedValues"], Is.Null);
        Assert.That(details["ordered"]!.Value<bool>(), Is.False);
        Assert.That(details["reference"], Is.Null);
        Assert.That(details["labels"]!["GOLD"]!.Value<string>(), Is.EqualTo("Gold"));
        Assert.That(details["labels"]!["SILVER"], Is.Null);
        Assert.That(details["descriptions"]!["GOLD"]!.Value<string>(), Is.EqualTo("Loyalty"));
        Assert.That(details["entries"], Is.Null);
        if (format == "avro") Assert.That(metadata["constraints"], Is.Null);
        else Assert.That(metadata["constraints"]!.Single()["value"]!.Values<string>(), Is.EqualTo(new[] { "GOLD", "SILVER" }));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void String_catalog_labels_use_compact_code_keys_without_a_second_membership_list(string format)
    {
        var codes = new[] { "STD", "PRE", "HON" };
        var values = codes.Select(ScalarValue.From).ToArray();
        var domain = new ValueDomain([new(values[0], "Standard"), new(values[1], "Premium"), new(values[2], "Honorary")], values);
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("category_code", "string", Required: true, ValueDomain: domain)), format);
        var schema = RoundTrip(result.Content!, format);
        var metadata = FieldMetadata(schema["fields"]![0]!, format);
        var expected = JObject.Parse("""{"labels":{"STD":"Standard","PRE":"Premium","HON":"Honorary"}}""");
        Assert.That(JToken.DeepEquals(metadata["valueDomain"], expected), Is.True, metadata.ToString());
        var membership = format == "avro" ? schema["fields"]![0]!["type"]!["symbols"] : metadata["constraints"]![0]!["value"];
        Assert.That(membership!.Values<string>(), Is.EqualTo(codes));
        if (format == "avro") Assert.That(metadata["constraints"], Is.Null);
    }

    [TestCase("avro", false)]
    [TestCase("avro", true)]
    [TestCase("spark-json", false)]
    [TestCase("spark-json", true)]
    public void Compact_annotations_preserve_catalog_order_and_values_outside_membership(string format, bool allAllowed)
    {
        var values = new[] { "A", "B", "C" }.Select(ScalarValue.From).ToArray();
        var allowed = allAllowed ? values : values.Take(2).ToArray();
        var domain = new ValueDomain([new(values[2], "Third"), new(values[0], "First", "Original description"), new(values[1])], allowed, ordered: true);
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("category", "string", Required: true, ValueDomain: domain)), format);
        var schema = RoundTrip(result.Content!, format);
        var metadata = FieldMetadata(schema["fields"]![0]!, format);
        var details = metadata["valueDomain"]!;
        Assert.That(details["entries"]!.Select(e => e["value"]!.Value<string>()), Is.EqualTo(new[] { "C", "A", "B" }));
        Assert.That(details["entries"]!.All(e => ((JObject)e).Properties().Count() == 1), Is.True);
        Assert.That(details["labels"]!["C"]!.Value<string>(), Is.EqualTo("Third"));
        Assert.That(details["descriptions"]!["A"]!.Value<string>(), Is.EqualTo("Original description"));
        Assert.That(details["ordered"]!.Value<bool>(), Is.True);
        var membership = format == "avro" ? schema["fields"]![0]!["type"]!["symbols"] : metadata["constraints"]![0]!["value"];
        Assert.That(membership!.Values<string>(), Is.EqualTo(allowed.Select(v => (string)v.Value!)));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Mixed_scalar_catalogs_keep_typed_entries_instead_of_conflating_JSON_keys(string format)
    {
        var number = ScalarValue.From(1);
        var text = ScalarValue.From("1");
        var domain = new ValueDomain([new(number, "Numeric one"), new(text, "Text one")], [number, text]);
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("code", "string", ValueDomain: domain)), format);
        var details = FieldMetadata(RoundTrip(result.Content!, format)["fields"]![0]!, format)["valueDomain"]!;
        Assert.That(details["labels"], Is.Null);
        Assert.That(details["entries"]![0]!["value"]!.Type, Is.EqualTo(JTokenType.Integer));
        Assert.That(details["entries"]![1]!["value"]!.Type, Is.EqualTo(JTokenType.String));
        Assert.That(details["entries"]!.Select(e => e["label"]!.Value<string>()), Is.EqualTo(new[] { "Numeric one", "Text one" }));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Compact_annotation_keys_preserve_case_empty_codes_and_JSON_escaping(string format)
    {
        var codes = new[] { "A", "a", "quote\"slash\\", "", "no_label" };
        var values = codes.Select(ScalarValue.From).ToArray();
        var domain = new ValueDomain(values.Select((v, i) => new ValueOption(v, i == 4 ? "" : "Label " + i)), values);
        var asset = Plain(new DataField("code", "string", ValueDomain: domain));
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        var details = FieldMetadata(RoundTrip(result.Content!, format)["fields"]![0]!, format)["valueDomain"]!;
        var labels = (JObject)details["labels"]!;
        Assert.That(labels.Properties().Select(p => p.Name), Is.EqualTo(codes.Take(4)));
        foreach (var index in Enumerable.Range(0, 4)) Assert.That(labels[codes[index]]!.Value<string>(), Is.EqualTo("Label " + index));
        Assert.That(details["entries"], Is.Null);
        Assert.That(details["descriptions"], Is.Null);
        Assert.That(SchemaDocumentSerializer.Serialize(asset, format).Content, Is.EqualTo(result.Content));
    }

    [Test]
    public void Native_enum_without_domain_annotations_has_no_redundant_metadata()
    {
        var code = ScalarValue.From("GOLD");
        var domain = new ValueDomain([new(code, "")], [code]);
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("category", "string", Required: true, ValueDomain: domain)), "avro");
        var schema = RoundTrip(result.Content!, "avro");
        Assert.That(schema.SelectTokens("$..packata"), Is.Empty);
        Assert.That(schema["fields"]![0]!["type"]!["symbols"]![0]!.Value<string>(), Is.EqualTo("GOLD"));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Explicit_null_and_empty_constraint_values_are_data_and_must_survive_compaction(string format)
    {
        var policy = new Dictionary<string, object?> { ["Null"] = null, ["EmptyList"] = Array.Empty<object>(), ["EmptyObject"] = new Dictionary<string, object>(), ["Zero"] = 0, ["False"] = false, ["EmptyText"] = "" };
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("id", "integer", Required: true,
            Constraints: [new("default", null), new("customPolicy", policy), new("enum", Array.Empty<object>())])), format);
        var constraints = FieldMetadata(RoundTrip(result.Content!, format)["fields"]![0]!, format)["constraints"]!;
        Assert.That(constraints[0]!["value"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That(JToken.DeepEquals(constraints[1]!["value"], JToken.FromObject(policy)), Is.True);
        Assert.That(constraints[2]!["value"], Is.TypeOf<JArray>());
        Assert.That(constraints[2]!["value"]!, Is.Empty);
    }

    [Test]
    public void Overridden_Avro_name_retains_source_identity_without_repeating_it()
    {
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("id", "integer", Required: true)), "avro", options: new(Name: "Renamed"));
        var schema = RoundTrip(result.Content!, "avro");
        Assert.That(schema["name"]!.Value<string>(), Is.EqualTo("Renamed"));
        var metadata = SchemaMetadata(schema, "avro");
        Assert.That(metadata["assetId"]!.Value<string>(), Is.EqualTo("members"));
        Assert.That(metadata["assetName"], Is.Null);
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Width_constraints_without_a_format_are_retained_only_when_the_target_widens(string format)
    {
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("small", "integer", Required: true,
            Constraints: [new("integerWidth", 8), new("signed", true)])), format);
        var schema = RoundTrip(result.Content!, format);
        if (format == "spark-json") Assert.That(schema.SelectTokens("$..packata.field"), Is.Empty);
        else
        {
            var constraints = FieldMetadata(schema["fields"]![0]!, format)["constraints"]!;
            Assert.That(constraints.Count(), Is.EqualTo(1));
            Assert.That(constraints[0]!["kind"]!.Value<string>(), Is.EqualTo("integerWidth"));
            Assert.That(constraints[0]!["value"]!.Value<int>(), Is.EqualTo(8));
        }
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Array_of_struct_fields_has_one_copy_of_each_nested_constraint(string format)
    {
        var asset = Plain(new DataField("records", "array", Required: true, Items: new("record", "object", Children:
            [new("code", "string", Required: true, Constraints: [new("minLength", 2)])])));
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        var schema = RoundTrip(result.Content!, format);
        var nativeItems = schema["fields"]![0]!["type"]![format == "avro" ? "items" : "elementType"]!;
        Assert.That(FieldMetadata(nativeItems["fields"]![0]!, format)["constraints"]![0]!["value"]!.Value<int>(), Is.EqualTo(2));
        Assert.That(schema.ToString().Split("minLength").Length - 1, Is.EqualTo(1));
        Assert.That(FieldMetadata(schema["fields"]![0]!, format)["items"]!["children"], Is.Null);
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Null_catalog_codes_and_quality_operands_are_preserved_but_absent_annotations_are_omitted(string format)
    {
        var missing = ScalarValue.From(null);
        var domain = new ValueDomain([new(missing, "Missing")], [missing]);
        var rule = new DataQualityRule("custom") { Expectations = [new(ExpectationOperator.Equal, QualityValue.From(null))] };
        var result = SchemaDocumentSerializer.Serialize(Plain(new DataField("code", "string", Nullable: true, ValueDomain: domain, QualityRules: [rule])), format);
        var metadata = FieldMetadata(RoundTrip(result.Content!, format)["fields"]![0]!, format);
        Assert.That(metadata["valueDomain"]!["entries"]![0]!["value"]!.Type, Is.EqualTo(JTokenType.Null));
        var quality = metadata["qualityRules"]![0]!;
        Assert.That(quality["Description"], Is.Null);
        Assert.That(quality["Metadata"], Is.Null);
        Assert.That(quality["Expectations"]![0]!["Operand"]!["Value"]!["Value"]!.Type, Is.EqualTo(JTokenType.Null));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Native_parser_round_trip_preserves_keys_relationships_and_typed_constraint_values(string format)
    {
        var members = Members(
            new("member_id", "integer", Required: true, Constraints: [new("primaryKey", true), new("unique", true), new("minimum", 1)]),
            new("category_code", "integer", Nullable: true, Constraints: [new("enum", new object?[] { 1, 2, null })]));
        var result = SchemaDocumentSerializer.Serialize(members, format);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var schema = RoundTrip(result.Content!, format);
        var metadata = SchemaMetadata(schema, format);
        Assert.That(metadata["primaryKey"]!.Values<string>(), Is.EqualTo(new[] { "member_id" }));
        Assert.That(metadata["relationships"]![0]!["targetAsset"]!.Value<string>(), Is.EqualTo("categories"));
        Assert.That(metadata["relationships"]![0]!["fields"]!.Values<string>(), Is.EqualTo(new[] { "category_code" }));
        Assert.That(metadata["allowAdditionalProperties"]!.Value<bool>(), Is.False);
        Assert.That(metadata["enforcePropertyOrder"]!.Value<bool>(), Is.True);
        Assert.That(metadata["fieldMatching"], Is.Null);
        var field = FieldMetadata(schema["fields"]![0]!, format);
        Assert.That(field["required"], Is.Null);
        Assert.That(field["nullable"], Is.Null);
        Assert.That(field["constraints"]!.Any(c => c["kind"]!.Value<string>() == "primaryKey"), Is.False);
        Assert.That(field["constraints"]![0]!["value"]!.Type, Is.EqualTo(JTokenType.Boolean));
        Assert.That(field["constraints"]![1]!["value"]!.Type, Is.EqualTo(JTokenType.Integer));
        var values = FieldMetadata(schema["fields"]![1]!, format)["constraints"]![0]!["value"]!;
        Assert.That(JToken.DeepEquals(values, new JArray(1, 2, JValue.CreateNull())), Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Severity is "Warning" or "Error"), Is.False);
        Assert.That(result.Diagnostics.Where(d => d.Code.StartsWith("SCHEMA_")).All(d => d.Severity == "Information"), Is.True);
        if (format == "spark-json") Assert.That(schema["fields"]![1]!["metadata"]!["packata"]!["schema"], Is.Null,
            "Asset metadata is anchored once on the first top-level field.");
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Nested_item_and_map_constraints_and_value_domain_labels_are_preserved(string format)
    {
        var code = ScalarValue.From("GOLD");
        var domain = new ValueDomain([new(code, "Gold member", "Loyalty category")], [code]);
        var array = new DataField("tags", "array", Items: new("tag", "string", Constraints: [new("minLength", 2)]));
        var map = new DataField("labels", "map", Map: new(new("key", "string"), new("value", "string", ValueDomain: domain)));
        var asset = Members(new DataField("nested", "object", Children: [array, map]));
        var result = SchemaDocumentSerializer.Serialize(asset, format);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        var schema = RoundTrip(result.Content!, format);
        var rootField = FieldMetadata(schema["fields"]![0]!, format);
        Assert.That(rootField["children"], Is.Null, "Nested fields have their own native metadata slots.");
        var children = schema["fields"]![0]!["type"]!["fields"]!;
        Assert.That(FieldMetadata(children[0]!, format)["items"]!["constraints"]![0]!["value"]!.Value<int>(), Is.EqualTo(2));
        var valueMetadata = FieldMetadata(children[1]!, format)["map"]!["value"]!;
        Assert.That(valueMetadata["valueDomain"]!["labels"]!["GOLD"]!.Value<string>(), Is.EqualTo("Gold member"));
        Assert.That(valueMetadata["valueDomain"]!["allowedValues"], Is.Null);
        if (format == "spark-json") Assert.That(valueMetadata["constraints"]![0]!["value"]![0]!.Value<string>(), Is.EqualTo("GOLD"));
        else Assert.That(valueMetadata["constraints"], Is.Null, "Native Avro symbols already express membership.");
        Assert.That(SchemaDocumentSerializer.Serialize(asset, format).Content, Is.EqualTo(result.Content));
    }

    [Test]
    public void Supported_native_enum_has_no_limitation_but_exporter_default_gap_remains_a_warning()
    {
        var field = new DataField("status", "string", Required: true,
            Constraints: [new("enum", new[] { "GOLD", "SILVER" }), new("default", "GOLD")]);
        var asset = new DataAsset("members", "members", null, null, AssetKind.Table, new([field]));
        var result = SchemaDocumentSerializer.Serialize(asset, "avro");
        var warning = result.Diagnostics.Single();
        Assert.That(warning.Code, Is.EqualTo("SCHEMA_IMPLEMENTATION_GAP"));
        Assert.That(warning.Severity, Is.EqualTo("Warning"));
        Assert.That(warning.Message, Does.Contain("Avro supports explicit defaults").And.Contain("exporter"));
        var json = JObject.Parse(result.Content!);
        Assert.That(json["fields"]![0]!["default"], Is.Null);
        Assert.That(FieldMetadata(json["fields"]![0]!, "avro")["constraints"]![0]!["value"]!.Value<string>(), Is.EqualTo("GOLD"));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Uninterpreted_constraints_remain_metadata_with_explicit_implementation_gap(string format)
    {
        var field = new DataField("id", "integer", Required: true, Constraints: [new("customPolicy", new Dictionary<string, object?> { ["Threshold"] = 7, ["Values"] = new object?[] { true, "x", null } })]);
        var result = SchemaDocumentSerializer.Serialize(Members(field), format);
        Assert.That(result.Diagnostics.Single(d => d.Code == "SCHEMA_IMPLEMENTATION_GAP").Severity, Is.EqualTo("Warning"));
        var parsed = RoundTrip(result.Content!, format);
        var value = FieldMetadata(parsed["fields"]![0]!, format)["constraints"]![0]!["value"]!;
        Assert.That(value["Threshold"]!.Value<int>(), Is.EqualTo(7));
        Assert.That(value["Values"]![2]!.Type, Is.EqualTo(JTokenType.Null));
    }

    [TestCase("avro")]
    [TestCase("spark-json")]
    public void Unresolved_membership_references_are_preserved_without_inventing_values(string format)
    {
        var field = new DataField("category", "string", ValueDomain: new(reference: new("categories", encodedAs: ValueEncoding.Value)));
        var result = SchemaDocumentSerializer.Serialize(Members(field), format);
        var domain = FieldMetadata(RoundTrip(result.Content!, format)["fields"]![0]!, format)["valueDomain"]!;
        Assert.That(domain["allowedValues"], Is.Null);
        Assert.That(domain["reference"]!["resourceName"]!.Value<string>(), Is.EqualTo("categories"));
        Assert.That(result.Content, Does.Not.Contain("\"symbols\""));
    }
}
