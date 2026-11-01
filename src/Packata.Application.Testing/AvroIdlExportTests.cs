using System.Diagnostics;
using Avro;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public class AvroIdlExportTests
{
    private static DataAsset Asset(params DataField[] fields)
        => new("orders", "orders", null, null, AssetKind.Table, new(fields));

    [Test]
    public void Catalog_advertises_a_text_export_with_Avro_naming_options_and_no_profile()
    {
        var format = SchemaGenerationService.Catalog.Targets.Single(t => t.Id == "avro-idl");
        Assert.That(format.Operation, Is.EqualTo("export"));
        Assert.That(format.Category, Is.EqualTo("schema-document"));
        Assert.That(format.Extension, Is.EqualTo(".avdl"));
        Assert.That(format.Representation, Is.EqualTo("text"));
        Assert.That(format.Options, Does.Contain("schema-name").And.Contain("schema-namespace"));
        Assert.That(SchemaGenerationService.ValidateOptions(new(Operation: "export", OutputFormat: "avro-idl",
            DocumentOptions: new("Order", "sales")), null), Is.Null);
        Assert.That(SchemaGenerationService.ValidateOptions(new(Operation: "export", OutputFormat: "avro-idl", CompatibilityProfile: "avro-idl"), null), Is.Not.Null);
        Assert.That(SchemaGenerationService.ValidateOptions(new(Target: "avro-idl", Language: "plaintext"), null), Is.Not.Null);
    }

    [Test]
    public void Nullable_fields_do_not_gain_defaults_and_diagnostics_match_the_Avro_projection()
    {
        var asset = Asset(new("id", "integer", Format: "i64", Required: true),
            new("record", "string", Nullable: true),
            new("amount", "number", Required: true, Constraints: [new("precision", 12), new("scale", 2)]));
        var json = SchemaDocumentSerializer.Serialize(asset, "avro");
        var idl = SchemaDocumentSerializer.Serialize(asset, "avro-idl");
        Assert.That(idl.IsSuccessful, Is.True, string.Join(";", idl.Diagnostics));
        Assert.That(idl.Content, Does.Contain("union { null, string }"));
        Assert.That(idl.Content, Does.Contain("`record`;").And.Contain("decimal(12, 2)"));
        Assert.That(idl.Content, Does.Not.Contain(" = "));
        Assert.That(idl.Diagnostics, Is.EqualTo(json.Diagnostics.Select(d => d with { Target = "avro-idl" })));
        Assert.That(idl.SuggestedFilename, Is.EqualTo("schema.avdl"));
        Assert.That(idl.Content, Is.EqualTo(SchemaDocumentSerializer.Serialize(asset, "avro-idl").Content));
    }

    [Test]
    public void Mapping_collisions_and_unrepresentable_docs_fail_without_partial_output()
    {
        foreach (var asset in new[] {
            Asset(new("a-b", "string"), new("a_b", "string")), Asset(new DataField("bad", "vector")),
            Asset(new DataField("doc", "string", Description: "unsafe */ documentation")),
            Asset(new DataField("doc", "string", Description: " leading space")) })
        {
            var result = SchemaDocumentSerializer.Serialize(asset, "avro-idl");
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Severity == "Error"), Is.True);
        }
        Assert.Throws<OperationCanceledException>(() => SchemaDocumentSerializer.Serialize(Asset(new DataField("id", "integer")),
            "avro-idl", new CancellationToken(true)));
    }

    [Test]
    public async Task Both_descriptor_formats_and_asset_selection_use_the_shared_workflow()
    {
        const string dp = """{"resources":[{"name":"orders","path":"must-not-be-read.csv","schema":{"fields":[{"name":"id","type":"integer","constraints":{"required":true}}]}}]}""";
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
            """;
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Operation: "export", OutputFormat: "avro-idl");
        var first = await service.GenerateAsync(dp, options);
        var second = await service.GenerateAsync(odcs, options);
        Assert.That(first.IsSuccessful && second.IsSuccessful, Is.True);
        Assert.That(first.Content, Is.EqualTo(second.Content));
        if (Environment.GetEnvironmentVariable("PACKATA_AVRO_TOOLS_JAR") is not null)
            foreach (var source in new[] { dp, odcs })
            {
                var avro = await service.GenerateAsync(source, options with { OutputFormat = "avro" });
                var idl = await service.GenerateAsync(source, options);
                await AssertCompilerEquivalent(avro.Content!, idl.Content!);
            }
        var multiple = JObject.Parse(dp);
        ((JArray)multiple["resources"]!).Add(JObject.Parse("""{"name":"other","schema":{"fields":[{"name":"id","type":"integer"}]}}"""));
        foreach (var selection in new string?[] { null, "missing" })
        {
            var result = await service.GenerateAsync(multiple.ToString(), options with { AssetId = selection });
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Code == "GEN_ASSET"), Is.True);
        }
        Assert.That((await service.GenerateAsync(multiple.ToString(), options with { AssetId = "orders" })).IsSuccessful, Is.True);
    }

    [Test]
    public void Unsupported_schema_annotations_are_diagnosed_instead_of_rewritten()
    {
        var schema = JObject.Parse("""{"type":"record","name":"Order","fields":[],"bad property":true}""");
        Assert.Throws<NotSupportedException>(() => AvroIdlRenderer.Render(schema));
    }

    [Test, Category("AvroIdlInterop")]
    public async Task Apache_1_12_compiler_preserves_projected_nested_types_enums_logical_types_and_metadata()
    {
        var asset = Asset(new("record", "string", Required: true, Description: "An identifier\nwith documentation"),
            new("status", "string", Nullable: true, Constraints: [new("enum", new[] { "OPEN", "CLOSED" })]),
            new("amount", "number", Constraints: [new("precision", 12), new("scale", 2)]),
            new("when", "timestamp", Constraints: [new("timezone", false)]), new("day", "date"), new("time", "time"),
            new("instant", "timestamp", Constraints: [new("timezone", true)]),
            new("customer", "object", Children: [new("id", "integer", Format: "i64")]),
            new("a", "object", Children: [new("b", "object", Children: [new("id", "integer")])]),
            new("a_b", "object", Children: [new("id", "integer")]),
            new("ratio", "number", Format: "fp32"), new("total", "number", Format: "fp64"), new("enabled", "boolean"),
            new("tags", "array", Items: new("tag", "string", Nullable: true)),
            new("labels", "map", Map: new(new("key", "string"), new("value", "object", Nullable: true, Children: [new("label", "string")]))),
            new("defaulted", "string", Constraints: [new("default", "hello")]));
        var json = SchemaDocumentSerializer.Serialize(asset, "avro");
        var idl = SchemaDocumentSerializer.Serialize(asset, "avro-idl");
        Assert.That(idl.IsSuccessful, Is.True, string.Join(";", idl.Diagnostics));
        await AssertCompilerEquivalent(json.Content!, idl.Content!);
    }

    [Test, Category("AvroIdlInterop")]
    public async Task Apache_compiler_preserves_native_defaults_aliases_namespaces_and_union_order()
    {
        const string json = """
            {"type":"record","name":"Order","namespace":"sales","doc":"An order","aliases":["Purchase"],"fields":[
              {"name":"record","type":["string","null"],"default":"yes","aliases":["old"],"doc":"Field doc"},
              {"name":"status","type":{"type":"enum","name":"Status","namespace":"types","symbols":["enum","OPEN"],"default":"OPEN","aliases":["State"]}},
              {"name":"hash","type":{"type":"fixed","name":"Hash","size":4}},
              {"name":"empty","type":["null","long"],"default":null}
            ]}
            """;
        await AssertCompilerEquivalent(json, AvroIdlRenderer.Render(JObject.Parse(json)));
    }

    private static async Task AssertCompilerEquivalent(string json, string idl)
    {
        var jar = Environment.GetEnvironmentVariable("PACKATA_AVRO_TOOLS_JAR");
        if (string.IsNullOrEmpty(jar)) Assert.Ignore("Set PACKATA_AVRO_TOOLS_JAR to Apache avro-tools 1.12.0.jar; PACKATA_JAVA may specify java.");
        var directory = Path.Combine(Path.GetTempPath(), "packata-avro-idl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "schema.avdl");
            var output = Path.Combine(directory, "schema.avsc");
            await File.WriteAllTextAsync(source, idl);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("PACKATA_JAVA") ?? "java")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-jar", jar!, "idl", source, output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.That(process.ExitCode, Is.Zero, await stderr + await stdout + "\n" + idl);
            var expected = JToken.Parse(Schema.Parse(json).ToString());
            var actual = JToken.Parse(Schema.Parse(await File.ReadAllTextAsync(output)).ToString());
            Assert.That(JToken.DeepEquals(actual, expected), Is.True, actual + "\nExpected:\n" + expected);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
