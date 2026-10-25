using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.DataPackage.Validation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonCategories = Packata.DataPackage.Serialization.Json.CategoriesConverter;
using JsonMetadata = Packata.DataPackage.Serialization.Json.CategoryMetadataConverter;
using YamlCategories = Packata.DataPackage.Serialization.Yaml.CategoriesConverter;
using YamlMetadata = Packata.DataPackage.Serialization.Yaml.CategoryMetadataConverter;

namespace Packata.DataPackage.Testing;

public class CategoryTableTests
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    { ContractResolver = new CamelCasePropertyNamesContractResolver(), Converters = [new JsonCategories(), new JsonMetadata()] };
    private static readonly IDeserializer YamlReader = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new YamlCategories()).WithTypeConverter(new YamlMetadata()).Build();
    private static readonly ISerializer YamlWriter = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new YamlCategories()).WithTypeConverter(new YamlMetadata()).Build();

    private const string Descriptor = """
        {"resources":[
          {"name":"data","type":"table","data":[],"schema":{"fields":[
            {"name":"fruit","type":"string","categories":{"resource":"fruit-codes"}}
          ]}},
          {"name":"fruit-codes","type":"table","path":"fruit-codes.csv",
           "categoryFieldMap":{"value":"code","label":"name","ordered":false},
           "schema":{"fields":[
             {"name":"code","type":"string","constraints":{"required":true,"unique":true}},
             {"name":"name","type":"string","constraints":{"unique":true}}
           ]}}
        ]}
        """;

    private static DataPackage Load(string input, SerializationFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        return new DataPackageFactory().LoadFromStream(stream, format);
    }

    [TestCase(SerializationFormat.Json)]
    [TestCase(SerializationFormat.Yaml)]
    public void Full_descriptor_loads_and_maps_same_package_references_without_reading_category_files(SerializationFormat format)
    {
        var package = Load(Descriptor, format);
        Assert.That(DataPackageValidator.Validate(package).Issues, Is.Empty);
        var categories = package.Resources[0].Schema!.Fields[0].Categories!;
        Assert.That(categories.Reference!.Resource, Is.EqualTo("fruit-codes"));
        Assert.That(categories.Reference.Package, Is.Null);
        Assert.That(categories.Reference.EncodedAs, Is.Null);
        Assert.That(categories, Is.Empty);
        var map = package.Resources[1].CategoryFieldMap!;
        Assert.That((map.Value, map.Label, map.Ordered), Is.EqualTo(("code", "name", false)));
        var contract = new DataPackageMapper().Map(package).RequireValue();
        var field = contract.Assets[0].Schema!.Fields[0];
        Assert.That(field.Extensions["datapackage"]["categories"], Is.Not.SameAs(categories.Reference));
        Assert.That(((IReadOnlyDictionary<string, object?>)field.Extensions["datapackage"]["categories"]!)["resource"], Is.EqualTo(categories.Reference.Resource));
        Assert.That(contract.Assets[1].Extensions["datapackage"]["categoryFieldMap"], Is.Not.SameAs(map));
        Assert.That(field.Constraints, Is.Empty, "References must not become empty or expanded DDL enum constraints.");
    }

    [TestCase(null)]
    [TestCase(false)]
    [TestCase(true)]
    public void Both_formats_round_trip_reference_options_and_all_ordering_states(bool? ordered)
    {
        foreach (var packageName in new string?[] { null, "https://example.com/datapackage.json", "shared-codes" })
            foreach (var encodedAs in new string?[] { null, "value", "label" })
            {
                var descriptor = new CategoryDescriptor
                {
                    Categories = new CategoryTableReference { Resource = "fruit-codes", Package = packageName, EncodedAs = encodedAs },
                    CategoryFieldMap = new() { Value = "code", Label = "name", Ordered = ordered }
                };
                var json = JsonConvert.SerializeObject(descriptor, JsonSettings);
                var yaml = YamlWriter.Serialize(descriptor);
                foreach (var restored in new[] { JsonConvert.DeserializeObject<CategoryDescriptor>(json, JsonSettings)!, YamlReader.Deserialize<CategoryDescriptor>(yaml) })
                {
                    var reference = restored.Categories.Reference!;
                    Assert.That((reference.Resource, reference.Package, reference.EncodedAs), Is.EqualTo(("fruit-codes", packageName, encodedAs)));
                    Assert.That((restored.CategoryFieldMap.Value, restored.CategoryFieldMap.Label, restored.CategoryFieldMap.Ordered), Is.EqualTo(("code", "name", ordered)));
                }
                Assert.That(JObject.Parse(json)["categories"]!["encodedAs"] is null, Is.EqualTo(encodedAs is null));
                Assert.That(JObject.Parse(json)["categoryFieldMap"]!["ordered"] is null, Is.EqualTo(ordered is null));
            }
    }

    [TestCase("https://example.com/datapackage.json")]
    [TestCase("shared-codes")]
    [TestCase("a")]
    [TestCase("0")]
    [TestCase("a0._-b")]
    public void External_references_are_preserved_and_not_resolved_as_local_resources(string packageName)
    {
        foreach (var format in new[] { SerializationFormat.Json, SerializationFormat.Yaml })
        {
            var package = Load(Descriptor, format);
            package.Resources.RemoveAt(1);
            var reference = package.Resources[0].Schema!.Fields[0].Categories!.Reference!;
            reference.Package = packageName; reference.EncodedAs = "label";
            Assert.That(DataPackageValidator.Validate(package).Issues, Is.Empty);
            var field = new DataPackageMapper().Map(package).RequireValue().Assets[0].Schema!.Fields[0];
            var snapshot = (IReadOnlyDictionary<string, object?>)field.Extensions["datapackage"]["categories"]!;
            Assert.That(snapshot["resource"], Is.EqualTo(reference.Resource));
            Assert.That(snapshot["package"], Is.EqualTo(reference.Package));
        }
    }

    [TestCase("")]
    [TestCase(".codes")]
    [TestCase("_codes")]
    [TestCase("-codes")]
    [TestCase("Codes")]
    [TestCase("code name")]
    [TestCase("codes\n")]
    [TestCase("cödés")]
    [TestCase("code/slash")]
    [TestCase("١")]
    public void Invalid_registry_names_are_rejected_by_validation_and_mapping(string name)
    {
        var package = Load(Descriptor, SerializationFormat.Json);
        package.Resources[0].Schema!.Fields[0].Categories!.Reference!.Package = name;
        Assert.That(DataPackageValidator.Validate(package).Issues.Any(issue => issue.Path.EndsWith(".package")), Is.True);
        var mapped = new DataPackageMapper().Map(package);
        Assert.That(mapped.Diagnostics.Any(issue => issue.Code == "DP006"), Is.True);
        Assert.That(mapped.RequireValue().Assets[0].Schema!.Fields[0].ValueDomain, Is.Null);
    }

    [TestCase("resource")]
    [TestCase("map")]
    [TestCase("value-name")]
    [TestCase("missing-value")]
    [TestCase("label-name")]
    [TestCase("value-type")]
    [TestCase("label-type")]
    [TestCase("required")]
    [TestCase("unique")]
    [TestCase("label-unique")]
    [TestCase("focal-type")]
    [TestCase("tabular")]
    [TestCase("encodedAs")]
    [TestCase("missing-label")]
    [TestCase("empty-name")]
    [TestCase("missing-resource")]
    [TestCase("package")]
    public void Invalid_references_and_field_maps_produce_property_paths(string defect)
    {
        var package = Load(Descriptor, SerializationFormat.Json);
        var target = package.Resources[1];
        var reference = package.Resources[0].Schema!.Fields[0].Categories!.Reference!;
        switch (defect)
        {
            case "resource": reference.Resource = "missing"; break;
            case "map": target.CategoryFieldMap = null; break;
            case "value-name": target.CategoryFieldMap!.Value = "missing"; break;
            case "missing-value": target.CategoryFieldMap!.Value = null; break;
            case "label-name": target.CategoryFieldMap!.Label = "missing"; break;
            case "value-type": target.Schema!.Fields[0].Type = "boolean"; break;
            case "label-type": target.Schema!.Fields[1].Type = "integer"; break;
            case "required": target.Schema!.Fields[0].Constraints!.Remove<RequiredConstraint>(); break;
            case "unique": target.Schema!.Fields[0].Constraints!.Remove<UniqueConstraint>(); break;
            case "label-unique": target.Schema!.Fields[1].Constraints!.Remove<UniqueConstraint>(); break;
            case "focal-type": package.Resources[0].Schema!.Fields[0].Type = "integer"; break;
            case "tabular": target.Type = null; break;
            case "encodedAs": reference.EncodedAs = "code"; break;
            case "missing-label": reference.EncodedAs = "label"; target.CategoryFieldMap!.Label = null; break;
            case "empty-name": reference.Resource = " "; break;
            case "missing-resource": reference.Resource = null; break;
            case "package": reference.Package = "file:///tmp/datapackage.json"; break;
        }
        var issues = DataPackageValidator.Validate(package).Issues;
        Assert.That(issues, Is.Not.Empty, defect);
        Assert.That(issues.Any(x => x.Path.Contains(".categories") || x.Path.Contains(".categoryFieldMap")), Is.True, defect);
    }

    [TestCase("{\"categories\":{\"resource\":1}}")]
    [TestCase("{\"categories\":{\"resource\":\"codes\",\"package\":false}}")]
    [TestCase("{\"categories\":{\"resource\":\"codes\",\"encodedAs\":1}}")]
    [TestCase("{\"categoryFieldMap\":{\"value\":1}}")]
    [TestCase("{\"categoryFieldMap\":{\"value\":\"code\",\"label\":2}}")]
    [TestCase("{\"categoryFieldMap\":{\"value\":\"code\",\"ordered\":\"false\"}}")]
    public void Metadata_parsing_rejects_wrong_property_types(string input)
    {
        Assert.That(() => JsonConvert.DeserializeObject<CategoryDescriptor>(input, JsonSettings), Throws.Exception);
        Assert.That(() => YamlReader.Deserialize<CategoryDescriptor>(input), Throws.Exception);
    }

    [Test]
    public void Plain_yaml_field_map_preserves_false_and_reference_defaults()
    {
        var result = YamlReader.Deserialize<CategoryDescriptor>("""
            categories:
              resource: fruit-codes
            categoryFieldMap:
              value: code
              ordered: false
            """);
        Assert.That(result.Categories.Reference!.EncodedAs, Is.Null);
        Assert.That(result.CategoryFieldMap.Label, Is.Null);
        Assert.That(result.CategoryFieldMap.Ordered, Is.False);
    }

    [Test]
    public void Optional_labels_and_independent_ordering_metadata_are_retained()
    {
        var package = Load(Descriptor, SerializationFormat.Json);
        var field = package.Resources[0].Schema!.Fields[0];
        field.CategoriesOrdered = true;
        field.Categories!.Reference!.EncodedAs = "label";
        Assert.That(DataPackageValidator.Validate(package).Issues, Is.Empty);
        var contract = new DataPackageMapper().Map(package).RequireValue();
        Assert.That(contract.Assets[0].Schema!.Fields[0].Extensions["datapackage"]["categoriesOrdered"], Is.True);
        Assert.That(package.Resources[1].CategoryFieldMap!.Ordered, Is.False);
        field.Categories.Reference.EncodedAs = null;
        package.Resources[1].CategoryFieldMap!.Label = null;
        Assert.That(DataPackageValidator.Validate(package).Issues, Is.Empty);
    }

    [Test]
    public void Reference_collection_rejects_inline_mutation_and_nullable_lists_remain_optional()
    {
        CategoryCollection categories = new CategoryTableReference { Resource = "codes" };
        Assert.That(() => categories.Add(new CategoryLabel("a")), Throws.InvalidOperationException);
        List<ICategory>? absent = null;
        CategoryCollection? converted = absent;
        Assert.That(converted, Is.Null);
    }

    public class CategoryDescriptor
    {
        public CategoryCollection Categories { get; set; } = [];
        public CategoryFieldMap CategoryFieldMap { get; set; } = new();
    }
}
