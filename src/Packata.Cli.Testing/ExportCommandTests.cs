using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class ExportCommandTests
{
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            properties:
              - name: order_id
                logicalType: string
                required: true
                nullable: true
                logicalTypeOptions:
                  maxLength: 50
              - name: amount
                logicalType: decimal
                logicalTypeOptions:
                  minimum: 0
                  exclusiveMinimum: true
              - name: created_at
                logicalType: date
              - name: customer
                logicalType: object
                properties:
                  - name: email
                    logicalType: string
                    required: true
              - name: tags
                logicalType: array
                logicalTypeOptions:
                  minItems: 1
                  uniqueItems: true
                items:
                  name: tag
                  logicalType: string
        """;

    [Test]
    public async Task Export_Odcs_ProducesDeterministicSchemaWithNestedConstraintsAndIndependentNullability()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("sales.odcs.yaml", Odcs);
        var first = await CliWorkspace.Run("schema", "export", source, "--output-format", "json-schema", "--asset", "orders");
        var second = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders");
        Assert.That(first.ExitCode, Is.Zero, first.Error);
        var schema = JObject.Parse(first.Output);
        var properties = schema["properties"]!;
        Assert.Multiple(() =>
        {
            Assert.That(first.Output, Is.EqualTo(second.Output));
            Assert.That(first.Error, Is.Empty);
            Assert.That((string?)schema["$schema"], Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
            Assert.That((string?)schema["title"], Is.EqualTo("orders"));
            Assert.That(schema["required"]!.Values<string>(), Is.EqualTo(new[] { "order_id" }));
            Assert.That(properties["order_id"]!["type"]!.Values<string>(), Is.EqualTo(new[] { "string", "null" }));
            Assert.That((int?)properties["order_id"]!["maxLength"], Is.EqualTo(50));
            Assert.That((decimal?)properties["amount"]!["exclusiveMinimum"], Is.Zero);
            Assert.That((string?)properties["created_at"]!["format"], Is.EqualTo("date"));
            Assert.That(properties["customer"]!["required"]!.Values<string>(), Is.EqualTo(new[] { "email" }));
            Assert.That((string?)properties["tags"]!["items"]!["type"], Is.EqualTo("string"));
            Assert.That((int?)properties["tags"]!["minItems"], Is.EqualTo(1));
            Assert.That((bool?)properties["tags"]!["uniqueItems"], Is.True);
            Assert.That(File.ReadAllText(source), Is.EqualTo(Odcs));
        });
    }

    [Test]
    public async Task Export_OutputFileProtectsExistingFilesAndSource()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Odcs);
        var output = Path.Combine(workspace.Path, "orders.schema.json");
        var first = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders", "-o", output);
        var content = File.ReadAllText(output);
        var second = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders", "--output", output);
        var overwriteSource = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders", "-o", source);
        Assert.Multiple(() =>
        {
            Assert.That(first.ExitCode, Is.Zero, first.Error);
            Assert.That(first.Output, Is.Empty);
            Assert.That((string?)JObject.Parse(content)["title"], Is.EqualTo("orders"));
            Assert.That(second.ExitCode, Is.EqualTo(3));
            Assert.That(overwriteSource.ExitCode, Is.EqualTo(3));
            Assert.That(File.ReadAllText(source), Is.EqualTo(Odcs));
            Assert.That(File.ReadAllText(output), Is.EqualTo(content));
        });
    }

    [TestCase("absent", "string", "does not exist")]
    [TestCase("orders", "map", "Unsupported logical type")]
    public async Task Export_RejectsUnknownDatasetsAndUnsupportedTypesWithoutOutput(string dataset, string type, string error)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", Odcs.Replace("logicalType: string", "logicalType: " + type));
        var result = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", dataset);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Does.Contain(error));
        });
    }

    [TestCase("xml", "orders")]
    [TestCase("json-schema", "")]
    public async Task Export_RejectsInvalidOptions(string format, string dataset)
    {
        var result = await CliWorkspace.Run("schema", "export", "missing.yaml", "-f", format, "--asset", dataset);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task Export_DataPackagePreservesPropertyCasingAndConstraints()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", """
            {"name":"sales","resources":[{"name":"orders","schema":{"fields":[
              {"name":"OrderID","type":"integer","constraints":{"required":true,"minimum":1}},
              {"name":"Price","type":"number","constraints":{"maximum":100}}
            ]}}]}
            """);
        var result = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var schema = JObject.Parse(result.Output);
        Assert.That((int?)schema["properties"]!["OrderID"]!["minimum"], Is.EqualTo(1));
        Assert.That((int?)schema["properties"]!["Price"]!["maximum"], Is.EqualTo(100));
    }

    [TestCase("string", "string", null)]
    [TestCase("boolean", "boolean", null)]
    [TestCase("integer", "integer", null)]
    [TestCase("decimal", "number", null)]
    [TestCase("number", "number", null)]
    [TestCase("date", "string", "date")]
    [TestCase("datetime", "string", "date-time")]
    [TestCase("timestamp", "string", "date-time")]
    [TestCase("array", "array", null)]
    [TestCase("object", "object", null)]
    public void Api_MapsLogicalTypes(string logicalType, string jsonType, string? format)
    {
        var schema = Export(new("value", logicalType));
        Assert.That((string?)schema["properties"]!["value"]!["type"], Is.EqualTo(jsonType));
        Assert.That((string?)schema["properties"]!["value"]!["format"], Is.EqualTo(format));
    }

    [TestCase("unique", true)]
    [TestCase("customConstraint", 1)]
    [TestCase("minimum", "yesterday")]
    [TestCase("maxLength", -1)]
    public void Api_RejectsConstraintsThatCannotBeSafelyConverted(string kind, object value)
    {
        Assert.Throws<JsonSchemaConversionException>(() => Export(new("value", "string", Constraints: [new(kind, value)])));
    }

    [Test]
    public async Task Export_RejectsAmbiguousDatasetAndMissingSchema()
    {
        using var workspace = new CliWorkspace();
        var duplicate = workspace.Write("duplicate.yaml", Odcs + "\n  - name: orders\n");
        var missing = workspace.Write("datapackage.json", CliWorkspace.PackageJson);
        var ambiguous = await CliWorkspace.Run("schema", "export", duplicate, "-f", "json-schema", "--asset", "orders");
        var noSchema = await CliWorkspace.Run("schema", "export", missing, "-f", "json-schema", "--asset", "customers");
        Assert.Multiple(() =>
        {
            Assert.That(ambiguous.ExitCode, Is.EqualTo(1));
            Assert.That(ambiguous.Error, Does.Contain("ambiguous"));
            Assert.That(ambiguous.Output, Is.Empty);
            Assert.That(noSchema.ExitCode, Is.EqualTo(1));
            Assert.That(noSchema.Error, Does.Contain("usable schema"));
            Assert.That(noSchema.Output, Is.Empty);
        });
    }

    [Test]
    public void Api_RejectsUnknownTypesAndDuplicateProperties()
    {
        Assert.Throws<JsonSchemaConversionException>(() => Export(new("value", "vector")));
        var asset = new DataAsset("orders", "orders", null, null, AssetKind.Table,
            new([new("id", "string"), new("id", "integer")]));
        Assert.Throws<JsonSchemaConversionException>(() => asset.ToJsonSchema());
    }

    [Test]
    public void Api_OptionalNonNullableFieldDoesNotBecomeRequired()
    {
        var schema = Export(new("value", "string", Required: false, Nullable: false));
        Assert.That(schema["required"], Is.Null);
        Assert.That((string?)schema["properties"]!["value"]!["type"], Is.EqualTo("string"));
    }

    [Test]
    public async Task Export_HelpDocumentsLongAndShortOptions()
    {
        var result = await CliWorkspace.Run("schema", "export", "--help");
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Does.Contain("--output-format").And.Contain("--asset").And.Contain("--output").And.Contain("json-schema"));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Export_RejectsTemporalBoundsInsteadOfDroppingThem(string lineEnding)
    {
        using var workspace = new CliWorkspace();
        var document = Odcs.ReplaceLineEndings(lineEnding).Replace("logicalType: date",
            $"logicalType: date{lineEnding}        logicalTypeOptions:{lineEnding}          minimum: '2020-01-01'");
        var source = workspace.Write("sales.yaml", document);
        var result = await CliWorkspace.Run("schema", "export", source, "-f", "json-schema", "--asset", "orders");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("minimum"));
        Assert.That(result.Output, Is.Empty);
    }

    private static JObject Export(DataField field)
    {
        var asset = new DataAsset("orders", "orders", null, null, AssetKind.Table, new([field]));
        using var stream = new MemoryStream();
        asset.ToJsonSchema().Serialize(stream);
        return JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
