using System.Collections;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Packata.DataPackage.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class CategoryTableMappingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void References_bind_in_either_order_and_preserve_omissions_and_metadata(bool lookupFirst)
    {
        var document = Load();
        document.Schema[0].CustomProperties.Add(new() { Vendor = "audit", Property = "note", Value = "preserved" });
        if (lookupFirst) document.Schema.Reverse();
        var result = document.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        var contract = result.RequireValue();
        var data = contract.Assets.Single(asset => asset.Name == "members");
        var table = contract.Assets.Single(asset => asset.Name == "categories");
        var reference = data.Schema!.Fields[0].ValueDomain!.Reference!;
        Assert.That((reference.ResourceName, reference.BoundAssetId, reference.EncodedAs), Is.EqualTo(("categories", "categories", (ValueEncoding?)null)));
        Assert.That(table.CategoryTable!.ValueField.Segments, Is.EqualTo(new[] { "key" }));
        Assert.That(table.CategoryTable.LabelField!.Segments, Is.EqualTo(new[] { "title" }));
        Assert.That(table.CategoryTable.Ordered, Is.False);
        var before = JsonSerializer.Serialize(new[] { data.Extensions["odcs"], table.Extensions["odcs"], data.Schema.Fields[0].Extensions["odcs"] });
        document.Schema.Single(schema => schema.Name == "categories").CustomProperties[0].Value = "changed";
        ((IDictionary)document.Schema.Single(schema => schema.Name == "members").Properties[0].CustomProperties[0].Value!)["resource"] = "changed";
        Assert.That(JsonSerializer.Serialize(new[] { data.Extensions["odcs"], table.Extensions["odcs"], data.Schema.Fields[0].Extensions["odcs"] }), Is.EqualTo(before));
        Assert.That(data.Schema.Fields[0].ValueDomain!.AllowedValues, Is.Null);
        Assert.That(data.Schema.Fields[0].Constraints, Is.Empty);
        Assert.That(contract.Endpoints.SelectMany(endpoint => ((PathLocation)endpoint.Location).Paths),
            Is.EquivalentTo(new[] { Path.Combine("./", "members.csv"), Path.Combine("./", "categories.csv") }));
    }

    [TestCase(null)]
    [TestCase(false)]
    [TestCase(true)]
    public void Optional_ordering_and_label_mapping_are_not_defaulted(bool? ordered)
    {
        var document = Load();
        document.Schema[1].CustomProperties[0].Value = ordered is null
            ? new Dictionary<string, object?> { ["value"] = "key" }
            : new Dictionary<string, object?> { ["value"] = "key", ["ordered"] = ordered };
        var table = document.ToCanonicalContract().RequireValue().Assets[1].CategoryTable!;
        Assert.That(table.Ordered, Is.EqualTo(ordered));
        Assert.That(table.LabelField, Is.Null);
    }

    [TestCase("value", ValueEncoding.Value)]
    [TestCase("label", ValueEncoding.Label)]
    public void Explicit_encoding_is_preserved(string encoding, ValueEncoding expected)
    {
        var document = Load();
        Reference(document)["encodedAs"] = encoding;
        var reference = document.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0].ValueDomain!.Reference!;
        Assert.That(reference.EncodedAs, Is.EqualTo(expected));
    }

    [Test]
    public void Other_vendors_and_unqualified_properties_are_preserved_without_interpretation()
    {
        var document = Load();
        var field = document.Schema[0].Properties[0];
        field.CustomProperties[0].Vendor = "other";
        field.CustomProperties.Add(new() { Property = "categories", Value = "not a declaration" });
        document.Schema[1].CustomProperties[0].Vendor = "other";
        var contract = document.ToCanonicalContract().RequireValue();
        Assert.That(contract.Assets[0].Schema!.Fields[0].ValueDomain, Is.Null);
        Assert.That(contract.Assets[1].CategoryTable, Is.Null);
        Assert.That((System.Collections.IEnumerable)contract.Assets[0].Schema!.Fields[0].Extensions["odcs"]["customProperties"]!, Has.Exactly(2).Items);
    }

    [TestCase("target", "Category table")]
    [TestCase("value", "explicit 'value'")]
    [TestCase("resource", "explicit 'resource'")]
    [TestCase("column", "Mapped column")]
    [TestCase("label-column", "Mapped column")]
    [TestCase("required", "required: true")]
    [TestCase("unique", "unique: true")]
    [TestCase("label-unique", "unique: true")]
    [TestCase("type", "logical type")]
    [TestCase("label-type", "type string")]
    [TestCase("ordered", "must be boolean")]
    [TestCase("encoding", "value or label")]
    [TestCase("shape", "mapping object")]
    [TestCase("target-type", "non-empty string")]
    [TestCase("duplicate-reference", "Duplicate")]
    [TestCase("duplicate-map", "Duplicate")]
    [TestCase("enum", "conflict")]
    [TestCase("label-without-column", "requires a mapped")]
    [TestCase("not-table", "requires a table")]
    [TestCase("map-shape", "mapping object")]
    public void Invalid_declarations_produce_actionable_mapping_errors(string kind, string expected)
    {
        var document = Load();
        var consumer = document.Schema[0].Properties[0];
        var lookup = document.Schema[1];
        var map = (IDictionary)lookup.CustomProperties[0].Value!;
        switch (kind)
        {
            case "target": Reference(document)["resource"] = "absent"; break;
            case "value": map.Remove("value"); break;
            case "resource": Reference(document).Remove("resource"); break;
            case "column": map["value"] = "absent"; break;
            case "label-column": map["label"] = "absent"; break;
            case "required": lookup.Properties[0].Required = false; break;
            case "unique": lookup.Properties[0].Unique = false; break;
            case "label-unique": lookup.Properties[1].Unique = false; break;
            case "type": document = Load(Fixture.ReplaceLineEndings("\n").Replace("name: code\n        logicalType: string", "name: code\n        logicalType: integer")); break;
            case "label-type": document = Load(Fixture.ReplaceLineEndings("\n").Replace("name: title\n        logicalType: string", "name: title\n        logicalType: integer")); break;
            case "ordered": map["ordered"] = "false"; break;
            case "encoding": Reference(document)["encodedAs"] = "invalid"; break;
            case "shape": consumer.CustomProperties[0].Value = new[] { "categories" }; break;
            case "target-type": Reference(document)["resource"] = 123; break;
            case "duplicate-reference": consumer.CustomProperties.Add(consumer.CustomProperties[0]); break;
            case "duplicate-map": lookup.CustomProperties.Add(lookup.CustomProperties[0]); break;
            case "enum": consumer.Enum = [new() { Value = "A" }]; break;
            case "label-without-column": map.Remove("label"); Reference(document)["encodedAs"] = "label"; break;
            case "not-table": lookup.PhysicalType = "view"; break;
            case "map-shape": lookup.CustomProperties[0].Value = false; break;
        }
        var result = document.ToCanonicalContract();
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Severity == MappingSeverity.Error && diagnostic.Message.Contains(expected)), Is.True,
            string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Test]
    public void Directory_bindings_require_explicit_physical_names()
    {
        var document = Load();
        document.Schema[1].PhysicalName = null;
        var result = document.ToCanonicalContract();
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "ODCS006" && diagnostic.SourcePath.EndsWith("physicalName")), Is.True);
    }

    [Test]
    public void Native_JSON_element_extension_values_are_supported()
    {
        var document = Load();
        document.Schema[0].Properties[0].CustomProperties[0].Value = JsonSerializer.SerializeToElement(new { resource = "categories" });
        document.Schema[1].CustomProperties[0].Value = JsonSerializer.SerializeToElement(new { value = "key", label = "title", ordered = false });
        Assert.That(document.ToCanonicalContract().Diagnostics, Is.Empty);
    }

    [Test]
    public void Quoted_boolean_metadata_is_diagnosed_and_native_yaml_round_trip_preserves_extensions()
    {
        Assert.That(Load(Fixture.Replace("ordered: false", "ordered: 'false'")).ToCanonicalContract().IsSuccessful, Is.False);
        var document = Load();
        var yaml = new Packata.OpenDataContract.Serialization.Yaml.DataContractSerializer().Serialize(document);
        var restored = Load(yaml).ToCanonicalContract().RequireValue();
        Assert.That(restored.Assets[1].CategoryTable!.Ordered, Is.False);
        Assert.That(restored.Assets[0].Schema!.Fields[0].ValueDomain!.Reference!.EncodedAs, Is.Null);
    }

    [Test]
    public void Equivalent_Data_Package_and_ODCS_declarations_map_to_equivalent_domains()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"resources":[{"name":"members","path":"members.csv","schema":{"fields":[
            {"name":"code","type":"string","categories":{"resource":"categories"}}]}},
            {"name":"categories","type":"table","path":"categories.csv","categoryFieldMap":{"value":"key","label":"title","ordered":false},
            "schema":{"fields":[{"name":"key","type":"string","constraints":{"required":true,"unique":true}},
            {"name":"title","type":"string","constraints":{"unique":true}}]}}]}
            """));
        var dp = new DataPackage.DataPackageFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue();
        var odcs = Load().ToCanonicalContract().RequireValue();
        Assert.That(odcs.Assets[0].Schema!.Fields[0].ValueDomain!.Reference, Is.EqualTo(dp.Assets[0].Schema!.Fields[0].ValueDomain!.Reference));
        Assert.That(odcs.Assets[1].CategoryTable!.ValueField.Segments, Is.EqualTo(dp.Assets[1].CategoryTable!.ValueField.Segments));
        Assert.That(odcs.Assets[1].CategoryTable!.LabelField!.Segments, Is.EqualTo(dp.Assets[1].CategoryTable!.LabelField!.Segments));
        Assert.That(odcs.Assets[1].CategoryTable!.Ordered, Is.EqualTo(dp.Assets[1].CategoryTable!.Ordered));
    }

    private static IDictionary Reference(DataContract document) => (IDictionary)document.Schema[0].Properties[0].CustomProperties[0].Value!;
    private static DataContract Load(string? yaml = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml ?? Fixture));
        return new DataContractFactory().LoadFromStream(stream);
    }

    private const string Fixture = """
        apiVersion: v3.2.0
        kind: DataContract
        id: members
        version: 1.0.0
        status: draft
        servers:
          - server: files
            type: local
            path: ./
            format: csv
        schema:
          - name: members
            physicalName: members.csv
            physicalType: table
            properties:
              - name: code
                logicalType: string
                customProperties:
                  - vendor: packata
                    property: categories
                    value: {resource: categories}
          - name: categories
            physicalName: categories.csv
            physicalType: table
            customProperties:
              - vendor: packata
                property: categoryFieldMap
                value: {value: key, label: title, ordered: false}
              - vendor: other
                property: categoryFieldMap
                value: {note: preserved}
            properties:
              - name: key
                logicalType: string
                required: true
                unique: true
              - name: title
                logicalType: string
                unique: true
        """;
}
