using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Validation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonCategories = Packata.DataPackage.Serialization.Json.CategoriesConverter;
using YamlCategories = Packata.DataPackage.Serialization.Yaml.CategoriesConverter;
using Packata.DataPackage.Serialization;
using System.Text;

namespace Packata.DataPackage.Testing;

public class InlineCategoriesTests
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = [new JsonCategories()]
    };
    private static readonly IDeserializer YamlReader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).WithTypeConverter(new YamlCategories()).Build();
    private static readonly ISerializer YamlWriter = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).WithTypeConverter(new YamlCategories()).Build();

    private static List<ICategory> Read(string input, bool yaml) => yaml
        ? YamlReader.Deserialize<List<ICategory>>(input)
        : JsonConvert.DeserializeObject<List<ICategory>>(input, JsonSettings)!;

    [TestCase(SerializationFormat.Json)]
    [TestCase(SerializationFormat.Yaml)]
    public void Full_descriptor_loading_preserves_date_like_string_codes(SerializationFormat format)
    {
        const string input = """
            {"resources":[{"name":"data","type":"table","data":[],"schema":{"fields":[
              {"name":"f","type":"string","categories":["2026-10-25T00:00:00Z","001"],"categoriesOrdered":true},
              {"name":"n","type":"integer","categories":[1,2147483648]}
            ]}}]}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var package = new DataPackageFactory().LoadFromStream(stream, format);
        var fields = package.Resources.Single().Schema!.Fields;
        Assert.That(fields[0].Categories!.Select(x => x.Value), Is.EqualTo(new[] {"2026-10-25T00:00:00Z","001"}));
        Assert.That(fields[0].CategoriesOrdered, Is.True);
        Assert.That(fields[1].Categories!.Select(x => x.Value), Is.EqualTo(new long[] {1,2147483648}));
        Assert.That(DataPackageValidator.Validate(package).IsValid, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Scalar_and_object_forms_preserve_types_optional_labels_and_order(bool yaml)
    {
        Assert.That(Read("[1, 2147483648, -9223372036854775808]", yaml).Select(x => x.Value),
            Is.EqualTo(new object[] { 1L, 2147483648L, long.MinValue }));
        Assert.That(Read("[\"001\", \"2026-10-25T00:00:00Z\", \"true\"]", yaml).Select(x => x.Value),
            Is.EqualTo(new[] { "001", "2026-10-25T00:00:00Z", "true" }));
        var values = Read("[{\"value\":\"001\",\"label\":\"One\"},{\"value\":\"A\"}]", yaml);
        Assert.That(values.Select(x => x.Value), Is.EqualTo(new[] { "001", "A" }));
        Assert.That(values.Select(x => x.Label), Is.EqualTo(new string?[] { "One", null }));
        Assert.That(Read("[{\"value\":2147483648}]", yaml).Single().Value, Is.EqualTo(2147483648L));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Converter_round_trips_preserve_representation_values_and_optional_labels(bool yaml)
    {
        foreach (var input in new[] { "[]", "[1,2147483648]", "[\"001\",\"true\",\"\"]",
            "[{\"value\":\"001\",\"label\":\"1\"},{\"value\":\"B\"}]", "[{\"value\":1},{\"value\":2,\"label\":\"Two\"}]" })
        {
            var categories = Read(input, yaml);
            var output = yaml ? YamlWriter.Serialize(categories) : JsonConvert.SerializeObject(categories, JsonSettings);
            var restored = Read(output, yaml);
            Assert.That(restored.Select(x => (x.GetType(), x.Value, x.Label)),
                Is.EqualTo(categories.Select(x => (x.GetType(), x.Value, x.Label))), output);
        }
    }

    [TestCase("[true]")]
    [TestCase("[null]")]
    [TestCase("[1.5]")]
    [TestCase("[9223372036854775808]")]
    [TestCase("[{\"label\":\"Missing value\"}]")]
    [TestCase("[{\"value\":1,\"label\":2}]")]
    [TestCase("[{\"value\":1,\"label\":null}]")]
    [TestCase("[{\"value\":false}]")]
    [TestCase("[{\"value\":[]}]")]
    [TestCase("[\"a\",{\"value\":\"b\"}]")]
    public void Malformed_entries_fail_with_diagnostics(string input)
    {
        Assert.That(() => Read(input, false), Throws.Exception.With.Message.Contains("[0]").Or.Message.Contains("[1]"));
        Assert.That(() => Read(input, true), Throws.Exception);
    }

    [Test]
    public void Validator_rejects_duplicates_wrong_types_and_enum_conflicts()
    {
        foreach (var field in new Field[]
        {
            new IntegerField { Type="integer", Categories=[new Category(1,"a"),new Category(1,"b")] },
            new IntegerField { Type="integer", Categories=[new Category(1,"same"),new Category(2,"same")] },
            new BooleanField { Type="boolean", Categories=[new CategoryLabel("true")] },
            new IntegerField { Type="integer", Categories=[new CategoryLabel("1")] },
            new StringField { Type="string", Categories=[new CategoryInteger(1)] },
            new StringField { Type="string", Categories=[new CategoryLabel("a")], Constraints=Enum("b") },
            new IntegerField { Type="integer", Categories=[new CategoryInteger(1)], Constraints=Enum("1") },
            new StringField { Type="string", CategoriesOrdered=false }
        })
        {
            field.Name = "f";
            var issues = DataPackageValidator.Validate(Package(field)).Issues;
            Assert.That(issues, Is.Not.Empty);
            Assert.That(issues.Any(x => x.Path.StartsWith("$.resources[0].schema.fields[0]")), Is.True);
        }
    }

    [TestCase(null)]
    [TestCase(false)]
    [TestCase(true)]
    public void Mapping_and_loading_preserve_ordering_states_and_enum_subsets(bool? ordered)
    {
        foreach(var yaml in new[] {false,true})
        {
            var suffix = ordered is null ? "" : ",\"categoriesOrdered\":" + ordered.Value.ToString().ToLowerInvariant();
            var input = "{\"name\":\"f\",\"type\":\"string\",\"categories\":[{\"value\":\"B\"},{\"value\":\"001\",\"label\":\"One\"}]" + suffix + "}";
            var field = yaml ? YamlReader.Deserialize<Field>(input) : JsonConvert.DeserializeObject<Field>(input,JsonSettings)!;
            Assert.That(field.CategoriesOrdered, Is.EqualTo(ordered));
            field.Constraints = Enum("001");
            Assert.That(DataPackageValidator.Validate(Package(field)).IsValid, Is.True);
            var mapped = new DataPackageMapper().Map(Package(field)).RequireValue().Assets.Single().Schema!.Fields.Single();
            Assert.That(mapped.Constraints.Single().Value, Is.EqualTo(new[] {"001"}));
            Assert.That(mapped.Extensions["datapackage"]["categoriesOrdered"], Is.EqualTo(ordered));
            Assert.That(mapped.Extensions["datapackage"]["categories"], Is.SameAs(field.Categories));
            field.Constraints = null;
            mapped = new DataPackageMapper().Map(Package(field)).RequireValue().Assets.Single().Schema!.Fields.Single();
            Assert.That(mapped.Constraints.Single().Value, Is.EqualTo(new[] {"B","001"}));
        }
    }

    private static FieldConstraintCollection Enum(params object[] values)
    { var constraints = new FieldConstraintCollection(); constraints.Add(new EnumConstraint(values)); return constraints; }

    private static DataPackage Package(Field field) => new()
    { Resources=[new Resource {Name="data", Type="table", Data=Array.Empty<object>(), Schema=new Schema {Fields=[field]} }] };
}
