using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Packata.Ossie.Testing;

public class OssieModelSerializerTests
{
    [TestCase(OssieSerializationFormat.Json, OssieSerializationFormat.Json)]
    [TestCase(OssieSerializationFormat.Yaml, OssieSerializationFormat.Yaml)]
    [TestCase(OssieSerializationFormat.Json, OssieSerializationFormat.Yaml)]
    [TestCase(OssieSerializationFormat.Yaml, OssieSerializationFormat.Json)]
    public void RoundTripRetainsModelContent(OssieSerializationFormat inputFormat, OssieSerializationFormat outputFormat)
    {
        var fixtureDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtureDirectory, "retail.json")));
        var extension = inputFormat == OssieSerializationFormat.Json ? "json" : "yaml";
        using var input = File.OpenRead(Path.Combine(fixtureDirectory, $"retail.{extension}"));
        var model = OssieModelSerializer.Deserialize(input, inputFormat);
        using var output = new MemoryStream();
        OssieModelSerializer.Serialize(model, output, outputFormat);
        output.Position = 0;
        var reloaded = OssieModelSerializer.Deserialize(output, outputFormat);
        var actual = JsonNode.Parse(SerializeJson(reloaded));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(input.CanRead && output.CanWrite, Is.True);
            Assert.That(JsonNode.DeepEquals(expected, actual), Is.True, actual?.ToJsonString());
            Assert.That(reloaded.Datasets[0].Fields![2].DataType, Is.EqualTo(OssieDataType.Date));
            Assert.That(reloaded.Datasets[0].Fields![3].Expression.Dialects![0].Expression, Is.EqualTo("raw_amount + 0\n-- opaque"));
            Assert.That(reloaded.AiContext!.Details!.AdditionalProperties!["ranking"].ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(reloaded.Datasets[0].AiContext!.Text, Is.EqualTo("One row per order."));
            Assert.That(reloaded.NonportableExtensions![0].Data["large"].GetRawText(), Is.EqualTo("987654321012345678901234567890"));
        }
    }

    [TestCase(OssieSerializationFormat.Json, """{"version":"0.2.0.dev0","semantic_model":[]}""")]
    [TestCase(OssieSerializationFormat.Yaml, "version: 0.3.0\nsemantic_model: []")]
    public void UnsupportedVersionPrecedesShapeValidation(OssieSerializationFormat format, string input)
    {
        var exception = Assert.Throws<OssieDocumentException>(() => Deserialize(input, format));
        Assert.That(exception!.Path, Is.EqualTo("$/version"));
        Assert.That(exception.Message, Does.Contain("Unsupported Ossie version").And.Contain("0.3.0.dev0"));
    }

    [TestCase("""{"name":"model","datasets":[{"name":"d","source":"t"}]}""", "$/version")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[]}""", "$/datasets")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":12}]}""", "$/datasets/0/source")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","fields":[{"name":"f","expression":{}}]}]}""", "$/datasets/0/fields/0/expression")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","ai_context":{"examples":[3]}}]}""", "$/datasets/0/ai_context/examples/0")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","nonportable_extensions":[{"vendor_name":"v","data":[]}]}]}""", "$/datasets/0/nonportable_extensions/0/data")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","fields":[{"name":"f","expression":{"sql":"x"},"datatype":"string"}]}]}""", "$/datasets/0/fields/0/datatype")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[],"datasets":[]}""", "$/datasets")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d"}]}""", "$/datasets/0/source")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","primary_key":["id"],"fields":[]}]}""", "$/datasets/0/primary_key/0")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","primary_key":["id","ID"],"fields":[{"name":"id","expression":{"sql":"src_id"}}]}]}""", "$/datasets/0/primary_key/1")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","fields":[{"name":"id","expression":{"sql":"id"}}],"metrics":[{"name":"ID","expression":{"sql":"COUNT(*)"}}]}]}""", "$/datasets/0/metrics/0/name")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t"}],"relationships":[{"name":"r","from":"d","to":"missing","from_columns":["id"],"to_columns":["id"]}]}""", "$/relationships/0/to")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t","fields":[{"name":"id","expression":{"sql":"id"}}]}],"relationships":[{"name":"r","from":"d","to":"D","from_columns":["ID"],"to_columns":["id"]}]}""", "$/relationships/0/to_columns")]
    [TestCase("""{"version":"0.3.0.dev0","name":"model","datasets":[{"name":"d","source":"t"}],"relationships":[{"name":"r","from":"d","to":"d","from_columns":["id","id2"],"to_columns":["id"]}]}""", "$/relationships/0/to_columns")]
    public void InvalidJsonReportsDeclarationPath(string json, string path)
        => Assert.That(Assert.Throws<OssieDocumentException>(() => Deserialize(json, OssieSerializationFormat.Json))!.Path,
            Is.EqualTo(path));

    [TestCase("version: [", "$")]
    [TestCase("version: 0.3.0.dev0\n---\nname: other", "$")]
    [TestCase("version: 0.3.0.dev0\nname: model\ndatasets: [{name: d, source: true}]", "$/datasets/0/source")]
    [TestCase("version: 0.3.0.dev0\nname: model\ndatasets: [{name: d, source: t}]\nnonportable_extensions: [{vendor_name: v, data: {value: .nan}}]", "$/nonportable_extensions/0/data/value")]
    [TestCase("version: 0.3.0.dev0\nname: model\ndatasets: [{name: d, source: t}]\nnonportable_extensions: [{vendor_name: v, data: {true: value}}]", "$/nonportable_extensions/0/data")]
    public void InvalidYamlReportsDeclarationPath(string yaml, string path)
        => Assert.That(Assert.Throws<OssieDocumentException>(() => Deserialize(yaml, OssieSerializationFormat.Yaml))!.Path,
            Is.EqualTo(path));

    [Test]
    public void DeeplyNestedYamlIsRejectedDuringParsing()
    {
        var yaml = new string('[', 65) + "0" + new string(']', 65);
        var exception = Assert.Throws<OssieDocumentException>(() => Deserialize(yaml, OssieSerializationFormat.Yaml));
        Assert.That(exception!.Message, Does.Contain("YAML nesting"));
    }

    [Test]
    public void InvalidModelIsRejectedBeforeWriting()
    {
        using var stream = new MemoryStream();
        var model = new OssieModel { Name = "model", Datasets = [] };

        var exception = Assert.Throws<OssieDocumentException>(() =>
            OssieModelSerializer.Serialize(model, stream, OssieSerializationFormat.Yaml));

        Assert.That(exception!.Path, Is.EqualTo("$/datasets"));
        Assert.That(stream.Length, Is.Zero);
    }

    private static OssieModel Deserialize(string input, OssieSerializationFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        return OssieModelSerializer.Deserialize(stream, format);
    }

    private static byte[] SerializeJson(OssieModel model)
    {
        using var stream = new MemoryStream();
        OssieModelSerializer.Serialize(model, stream, OssieSerializationFormat.Json);
        return stream.ToArray();
    }
}
