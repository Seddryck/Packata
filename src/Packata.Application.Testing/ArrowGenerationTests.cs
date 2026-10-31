using System.Diagnostics;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Arrow;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public class ArrowGenerationTests
{
    [TestCase("csharp")]
    [TestCase("python")]
    public async Task Arrow_targets_are_deterministic_and_share_nested_type_parameters(string language)
    {
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Target: "arrow", Language: language, TypeName: "Orders");
        var result = await service.GenerateAsync(SchemaGenerationTests.Descriptor, options);
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo((await service.GenerateAsync(SchemaGenerationTests.Descriptor, options)).Content));
        Assert.That(result.Language, Is.EqualTo(language));
        Assert.That(result.Diagnostics.Any(d => d.Code == "ARROW_CONSTRAINTS"), Is.True);
        if (language == "csharp")
        {
            var schema = (Schema)SchemaGenerationTests.Compile(result.Content!).GetType("Orders")!.GetMethod("Create")!.Invoke(null, null)!;
            Assert.That(schema.GetFieldByIndex(0).DataType, Is.TypeOf<UInt64Type>());
            Assert.That(((Decimal128Type)schema.GetFieldByIndex(1).DataType).Precision, Is.EqualTo(18));
            Assert.That(schema.GetFieldByIndex(2).IsNullable, Is.True);
            Assert.That(schema.GetFieldByIndex(2).Metadata["packata.required"], Is.EqualTo("true"));
            Assert.That(schema.GetFieldByIndex(7).DataType, Is.TypeOf<StructType>());
            Assert.That(schema.GetFieldByIndex(8).DataType, Is.TypeOf<ListType>());
            using var stream = new MemoryStream();
            using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true)) { await writer.WriteStartAsync(); await writer.WriteEndAsync(); }
            stream.Position = 0;
            using var reader = new ArrowStreamReader(stream);
            Assert.That((await reader.GetSchema()).FieldsList.Count, Is.EqualTo(schema.FieldsList.Count));
        }
        else await VerifyPython(result.Content!);
    }

    [Test]
    public async Task Unsupported_precision_offset_and_language_never_render_partial_code()
    {
        var service = new SchemaGenerationService();
        foreach (var language in new[] { "csharp", "python" })
        {
            var invalid = await service.GenerateAsync(SchemaGenerationTests.Descriptor.Replace("precision: 18", "precision: 90"), new(Target: "arrow", Language: language));
            Assert.That(invalid.Content, Is.Null);
            Assert.That(invalid.Diagnostics.Any(d => d.Code == "ARROW_TYPE" && d.Severity == "Error"), Is.True);
        }
        foreach (var language in new string?[] { null, "rust" })
            Assert.That((await service.GenerateAsync(SchemaGenerationTests.Descriptor, new(Target: "arrow", Language: language))).Diagnostics[0].Code, Is.EqualTo("GEN_OPTIONS"));
    }

    [TestCase("csharp-arrow")]
    [TestCase("python-arrow")]
    public async Task Custom_profiles_expose_the_same_prepared_expression(string profile)
    {
        var result = await new SchemaGenerationService().GenerateAsync(SchemaGenerationTests.Descriptor,
            new(Profile: profile), new("custom.scriban", "{{ model.target.schema_expression }}"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Does.Contain(profile == "python-arrow" ? "pa.schema(" : "new Schema("));
    }

    [Test]
    public void Canonical_map_metadata_and_dictionary_extension_decisions_are_explicit()
    {
        var map = new DataField("values", "object", Children: [new("key", "string"), new("value", "integer", Nullable: true)],
            Extensions: ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["type"] = "map" }));
        var schema = ArrowSchemaMapper.Map(new([map], Extensions: ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["owner"] = "sales" })));
        Assert.That(schema.Schema!.GetFieldByIndex(0).DataType, Is.TypeOf<MapType>());
        Assert.That(schema.Schema.Metadata["owner"], Is.EqualTo("sales"));
        var dictionary = map with { Extensions = ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["type"] = "dictionary" }) };
        Assert.That(ArrowSchemaMapper.Map(new([dictionary])).Schema, Is.Null);
        var extension = map with { Extensions = ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["ARROW:extension:name"] = "custom" }) };
        Assert.That(() => ArrowSchemaMapper.Map(new([extension])), Throws.TypeOf<NotSupportedException>());
    }

    private static async Task VerifyPython(string content)
    {
        var python = Environment.GetEnvironmentVariable("PACKATA_TEST_PYTHON");
        if (python is null) { Assert.Ignore("Set PACKATA_TEST_PYTHON to a Python interpreter with PyArrow 23+ for interoperability."); return; }
        var path = Path.Combine(Path.GetTempPath(), "packata-arrow-" + Guid.NewGuid().ToString("N") + ".py");
        try
        {
            await File.WriteAllTextAsync(path, content + "\nassert Orders.field('id').type == pa.uint64()\nassert Orders.field('amount').type == pa.decimal128(18, 2)\nassert Orders.field('class').nullable\nassert Orders.field('class').metadata[b'packata.required'] == b'true'\nassert pa.types.is_struct(Orders.field('customer').type)\nassert pa.types.is_list(Orders.field('tags').type)\nassert Orders.field('occurred').type == pa.timestamp('us')\n");
            var csharp = await new SchemaGenerationService().GenerateAsync(SchemaGenerationTests.Descriptor, new(Target: "arrow", Language: "csharp", TypeName: "Orders"));
            var schema = (Schema)SchemaGenerationTests.Compile(csharp.Content!).GetType("Orders")!.GetMethod("Create")!.Invoke(null, null)!;
            using (var stream = File.Create(path + ".arrow"))
            using (var writer = new ArrowStreamWriter(stream, schema)) { await writer.WriteStartAsync(); await writer.WriteEndAsync(); }
            await File.AppendAllTextAsync(path, "\nwith pa.ipc.open_stream(__file__ + '.arrow') as reader:\n    assert Orders.equals(reader.schema, check_metadata=True), (Orders, reader.schema)\n");
            var start = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.That(process.ExitCode, Is.Zero, await error);
        }
        finally { File.Delete(path); File.Delete(path + ".arrow"); }
    }
}
