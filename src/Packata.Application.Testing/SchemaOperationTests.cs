using NUnit.Framework;
using Packata.Application.Generation;
namespace Packata.Application.Testing;
public class SchemaOperationTests
{
    [Test]
    public async Task Json_schema_uses_existing_converter_and_infers_only_unambiguous_asset()
    {
        var document = ContractDocument.Parse("""{"resources":[{"name":"orders","schema":{"fields":[{"name":"id","type":"integer","constraints":{"required":true}},{"name":"nested","type":"object","fields":[{"name":"name","type":"string"}]}]}}]}""");
        var expected = ContractExportService.JsonSchema(document, "orders");
        var actual = await new SchemaGenerationService().GenerateAsync(document, new(Operation: "export", OutputFormat: "json-schema"));
        Assert.That(actual.IsSuccessful, Is.True, string.Join(";", actual.Diagnostics));
        Assert.That(actual.Content, Is.EqualTo(expected.Content));
        Assert.That(actual.MediaType, Is.EqualTo("application/schema+json"));
    }
    [Test]
    public void Operation_validation_rejects_wrong_selectors_and_missing_language()
    {
        foreach (var options in new GenerationOptions[] {
            new(Target: "record"), new(Target: "record", Language: "python"),
            new(Target: "record", Language: "csharp", OutputFormat: "json-schema"),
            new(Operation: "export", OutputFormat: "json-schema", Language: "csharp"),
            new(Operation: "export", OutputFormat: "json-schema", CompatibilityProfile: "hudi"),
            new(Operation: "export", OutputFormat: "arrow-ipc", CompatibilityProfile: "lance"),
            new(Target: "record", Language: "csharp", Dialect: "sqlite") })
            Assert.That(SchemaGenerationService.ValidateOptions(options, null), Is.Not.Null, options.ToString());
        Assert.That(SchemaGenerationService.ValidateOptions(new(Operation: "export", OutputFormat: "arrow-ipc"), null), Is.Null);
        Assert.That(SchemaGenerationService.ValidateOptions(new(Target: "record", Language: "csharp"), null), Is.Null);
        Assert.That(SchemaGenerationService.ValidateOptions(new(Language: "python"), new("custom.scriban", "hello")), Is.Null);
    }
}
