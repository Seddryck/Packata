using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.OpenDataContract.Mapping;
using DP = Packata.DataPackage;
using ODCS = Packata.OpenDataContract;

namespace Packata.Cli.Testing;

public class ValueDomainMappingTests
{
    [Test]
    public void Canonical_diff_can_snapshot_mapped_categories()
    {
        DataContract Create() => Map(new DP.StringField { Name = "code", Type = "string", Categories = [new DP.Category("A", "Alpha")] }).RequireValue();
        Assert.That(Create().Diff(Create()).Changes, Is.Empty);
    }
    [Test]
    public void Json_schema_export_uses_effective_values_without_labels_or_unresolved_expansion()
    {
        var a = ScalarValue.From("A");
        var b = ScalarValue.From("B");
        var schema = new DataAsset("data", "data", null, null, AssetKind.Table, new DataSchema(
        [
            new DataField("code","string",ValueDomain:new([new(a,"Alpha","Description"),new(b,"Beta")],[a])),
            new DataField("unknown","string",ValueDomain:new(reference:new("codes")))
        ])).ToJsonSchema();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(schema);
        Assert.That(json.GetProperty("properties").GetProperty("code").GetProperty("enum").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "A" }));
        Assert.That(json.GetProperty("properties").GetProperty("unknown").TryGetProperty("enum", out _), Is.False);
    }
    [TestCase("integer", "1", ScalarKind.Integer)]
    [TestCase("number", "0.1", ScalarKind.Number)]
    [TestCase("boolean", "true", ScalarKind.Boolean)]
    public void ODCS_yaml_values_keep_their_scalar_kinds(string type, string literal, ScalarKind kind)
    {
        var yaml = $"apiVersion: v3.2.0\nkind: DataContract\nid: sample\nversion: 1.0.0\nstatus: draft\nschema:\n  - name: data\n    properties:\n      - name: value\n        logicalType: {type}\n        enum:\n          - value: {literal}\n            label: Display\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var source = new ODCS.DataContractFactory().LoadFromStream(stream);
        var result = source.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.RequireValue().Assets[0].Schema!.Fields[0].ValueDomain!.Entries!.Single().Value.Kind, Is.EqualTo(kind));
    }
    private static MappingResult<DataContract> Map(DP.Field field, params DP.Resource[] additional) => new DataPackageMapper().Map(new DP.DataPackage
    { Name = "sample", Resources = [new DP.Resource { Name = "data", Type = "table", Data = Array.Empty<object>(), Schema = new DP.Schema { Fields = [field] } }, .. additional] });

    private static DataField Field(MappingResult<DataContract> result) => result.RequireValue().Assets[0].Schema!.Fields[0];

    [Test]
    public void Equivalent_yaml_inputs_provide_the_same_value_and_label_API_without_native_downcasts()
    {
        const string dp = """
            resources:
              - name: data
                type: table
                data: []
                schema:
                  fields:
                    - name: code
                      type: string
                      categories:
                        - value: '001'
                          label: First
                        - value: B
            """;
        const string odcs = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sample
            version: 1.0.0
            status: draft
            schema:
              - name: data
                properties:
                  - name: code
                    logicalType: string
                    enum:
                      - value: '001'
                        label: First
                      - value: B
            """;
        using var dpStream = new MemoryStream(Encoding.UTF8.GetBytes(dp));
        using var odcsStream = new MemoryStream(Encoding.UTF8.GetBytes(odcs));
        var left = new DP.DataPackageFactory().LoadFromStream(dpStream, SerializationFormat.Yaml).ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0];
        var right = new ODCS.DataContractFactory().LoadFromStream(odcsStream).ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(left.ValueDomain!.Entries!.Select(x => (x.Value, x.Label, x.Description)),
            Is.EqualTo(right.ValueDomain!.Entries!.Select(x => (x.Value, x.Label, x.Description))));
        Assert.That(left.ValueDomain.AllowedValues, Is.EqualTo(right.ValueDomain.AllowedValues));
        Assert.That(left.ValueDomain.Ordered, Is.Null); Assert.That(right.ValueDomain.Ordered, Is.Null);
        Assert.That(left.Constraints.Single().Value, Is.EqualTo(right.Constraints.Single().Value));
    }

    [TestCase(null)]
    [TestCase(false)]
    [TestCase(true)]
    public void DP_catalog_retains_excluded_entries_and_separate_enum_sequence(bool? ordered)
    {
        var native = new DP.StringField
        {
            Name = "code",
            Type = "string",
            CategoriesOrdered = ordered,
            Categories = [new DP.Category("A", "Alpha"), new DP.Category("B", "Beta"), new DP.Category("C")],
            Constraints = new DP.FieldConstraintCollection()
        };
        native.Constraints.Add(new DP.EnumConstraint(new object[] { "C", "A" }));
        var field = Field(Map(native));
        Assert.That(field.ValueDomain!.Entries!.Select(x => x.Value.Value), Is.EqualTo(new[] { "A", "B", "C" }));
        Assert.That(field.ValueDomain.AllowedValues!.Select(x => x.Value), Is.EqualTo(new[] { "C", "A" }));
        Assert.That(field.ValueDomain.Ordered, Is.EqualTo(ordered));
        Assert.That(field.Constraints.Single().Value, Is.EqualTo(new[] { "C", "A" }));
        native.Constraints.Clear();
        native.Constraints.Add(new DP.EnumConstraint(new object[] { "changed" }));
        Assert.That(field.Constraints.Single().Value, Is.EqualTo(new[] { "C", "A" }));
        native.Categories!.Add(new DP.Category("D", "Changed")); native.CategoriesOrdered = !ordered;
        Assert.That(field.ValueDomain.Entries, Has.Count.EqualTo(3));
        Assert.That(((IReadOnlyList<object?>)field.Extensions["datapackage"]["categories"]!), Has.Count.EqualTo(3));
    }

    [Test]
    public void Scalar_enum_constraints_map_without_categories_including_number_boolean_and_null()
    {
        foreach (var (type, values, kinds) in new[]
        {
            ("number",new object?[]{1,0.1m},new[]{ScalarKind.Integer,ScalarKind.Number}),
            ("boolean",new object?[]{true,false},new[]{ScalarKind.Boolean,ScalarKind.Boolean}),
            ("any",new object?[]{"001",null},new[]{ScalarKind.String,ScalarKind.Null})
        })
        {
            var native = new DP.Field { Name = "value", Type = type, Constraints = new() };
            native.Constraints.Add(new DP.EnumConstraint(values));
            var domain = Field(Map(native)).ValueDomain!;
            Assert.That(domain.Entries!.Select(x => x.Value.Kind), Is.EqualTo(kinds));
            Assert.That(domain.AllowedValues!.Select(x => x.Value), Is.EqualTo(values));
        }
    }

    [Test]
    public void Reference_binding_does_not_resolve_rows_and_preserves_independent_ordering_and_explicit_subset()
    {
        var map = new DP.CategoryFieldMap { Value = "code", Label = "name", Ordered = false };
        var constraints = new DP.FieldConstraintCollection();
        constraints.Add(new DP.RequiredConstraint(true)); constraints.Add(new DP.UniqueConstraint(true));
        var labels = new DP.FieldConstraintCollection(); labels.Add(new DP.UniqueConstraint(true));
        var table = new DP.Resource
        {
            Name = "codes",
            Type = "table",
            Data = Array.Empty<object>(),
            CategoryFieldMap = map,
            Schema = new DP.Schema
            {
                Fields = [new DP.StringField {Name="code",Type="string",Constraints=constraints},
                new DP.StringField {Name="name",Type="string",Constraints=labels}]
            }
        };
        var reference = new DP.CategoryTableReference { Resource = "codes", EncodedAs = "label" };
        var native = new DP.StringField { Name = "fruit", Type = "string", Categories = reference, CategoriesOrdered = true };
        var result = Map(native, table);
        var field = Field(result);
        var domain = field.ValueDomain!;
        Assert.That(domain.Entries, Is.Null); Assert.That(domain.AllowedValues, Is.Null);
        Assert.That(domain.Ordered, Is.True); Assert.That(field.Constraints, Is.Empty);
        Assert.That(domain.Reference!.BoundAssetId, Is.EqualTo("codes"));
        Assert.That(domain.Reference.EncodedAs, Is.EqualTo(ValueEncoding.Label));
        var definition = result.RequireValue().Assets[1].CategoryTable!;
        Assert.That(definition.ValueField.Segments, Is.EqualTo(new[] { "code" }));
        Assert.That(definition.LabelField!.Segments, Is.EqualTo(new[] { "name" }));
        Assert.That(definition.Ordered, Is.False);
        Assert.That(result.RequireValue().Diff(result.RequireValue()).Changes, Is.Empty);
        reference.Resource = "changed"; map.Value = "changed"; map.Ordered = true;
        Assert.That(domain.Reference.ResourceName, Is.EqualTo("codes"));
        Assert.That(definition.ValueField.Segments, Is.EqualTo(new[] { "code" }));
        Assert.That(((IReadOnlyDictionary<string, object?>)field.Extensions["datapackage"]["categories"]!)["resource"], Is.EqualTo("codes"));
        native.Categories = new DP.CategoryTableReference { Resource = "codes" };
        native.Constraints = new(); native.Constraints.Add(new DP.EnumConstraint(new object[] { "A" }));
        // Restore the valid table descriptor; explicit membership does not require loading its data.
        map.Value = "code";
        var restricted = Field(Map(native, table));
        Assert.That(restricted.ValueDomain!.AllowedValues!.Select(x => x.Value), Is.EqualTo(new[] { "A" }));
        Assert.That(restricted.Constraints.Single().Value, Is.EqualTo(new[] { "A" }));
    }

    [TestCase("https://example.com/datapackage.json", PackageLocatorKind.DescriptorUri)]
    [TestCase("shared-codes", PackageLocatorKind.RegistryName)]
    public void External_reference_is_typed_and_never_bound_to_a_local_asset(string locator, PackageLocatorKind kind)
    {
        var domain = Field(Map(new DP.StringField { Name = "code", Type = "string", Categories = new DP.CategoryTableReference { Resource = "codes", Package = locator } })).ValueDomain!;
        Assert.That(domain.Reference!.Package!.Kind, Is.EqualTo(kind));
        Assert.That(domain.Reference.Package.Value, Is.EqualTo(locator));
        Assert.That(domain.Reference.BoundAssetId, Is.Null);
        Assert.That(domain.Reference.EncodedAs, Is.Null);
        Assert.That(domain.Reference.EffectiveEncoding, Is.EqualTo(ValueEncoding.Value));
        Assert.That(domain.AllowedValues, Is.Null);
    }

    [Test]
    public void Unsupported_structures_invalid_subsets_and_wrong_types_are_diagnosed_without_losing_constraints()
    {
        foreach (var (type, categories, values) in new (string, DP.CategoryCollection?, object?[])[]
        {
            ("string",null,[new Dictionary<string,object?>{["nested"]="value"}]),
            ("integer",null,[ulong.MaxValue]),
            ("string",[new DP.CategoryLabel("A")],["B"]),
            ("integer",[new DP.CategoryLabel("1")],[1])
        })
        {
            var native = new DP.Field { Name = "code", Type = type, Categories = categories, Constraints = new() };
            native.Constraints.Add(new DP.EnumConstraint(values));
            var result = Map(native);
            Assert.That(result.Diagnostics.Any(x => x.Code == "DP006"), Is.True);
            Assert.That(Field(result).ValueDomain, Is.Null);
            Assert.That(Field(result).Constraints.Single().Value, Is.EqualTo(values));
        }
        var missing = Map(new DP.StringField { Name = "code", Type = "string", Categories = new DP.CategoryTableReference { Resource = "missing" } });
        Assert.That(missing.Diagnostics.Any(x => x.Code == "DP006"), Is.True);
        Assert.That(Field(missing).ValueDomain!.Reference!.BoundAssetId, Is.Null);
        Assert.That(Field(missing).ValueDomain!.AllowedValues, Is.Null);
    }

    [Test]
    public void ODCS_rich_metadata_is_available_in_the_common_API_and_detached_from_native_mutations()
    {
        var entry = new ODCS.EnumerationValue { Value = "001", Label = "First", Description = "Description", Tags = ["active"] };
        var source = new ODCS.DataContract { Id = "sample", Schema = [new ODCS.SchemaObject { Name = "data", Properties = [new ODCS.SchemaProperty { Name = "code", Enum = [entry] }] }] };
        var field = source.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0];
        var option = field.ValueDomain!.Entries!.Single();
        Assert.That((option.Value.Value, option.Label, option.Description), Is.EqualTo(("001", "First", "Description")));
        entry.Value = "changed"; entry.Label = "changed"; entry.Description = "changed"; entry.Tags.Clear();
        Assert.That(option.Value.Value, Is.EqualTo("001"));
        Assert.That(option.Label, Is.EqualTo("First"));
        Assert.That(option.Description, Is.EqualTo("Description"));
        Assert.That(option.Extensions["odcs"]["tags"], Is.EqualTo(new[] { "active" }));
        Assert.That(field.Constraints.Single().Value, Is.EqualTo(new[] { "001" }));
    }
}
