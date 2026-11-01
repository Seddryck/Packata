using System.Diagnostics;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;
using Packata.Application.Generation;

namespace Packata.Application.Testing;

public class SchemaDocumentTests
{
    [Test]
    public async Task One_schema_message_roundtrips_without_batches_or_trailing_bytes()
    {
        var document = ContractDocument.Parse(SchemaGenerationTests.Descriptor);
        var result = await SchemaDocumentService.ExportAsync(document, new("arrow-ipc"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Representation, Is.EqualTo("binary"));
        Assert.That(result.SuggestedFilename, Is.EqualTo("orders.schema.ipc"));
        using var stream = new MemoryStream(result.Bytes!);
        using var reader = new ArrowStreamReader(stream);
        var schema = await reader.GetSchema();
        Assert.That(schema.FieldsList, Has.Count.EqualTo(9));
        Assert.That(schema.GetFieldByIndex(0).DataType, Is.TypeOf<UInt64Type>());
        Assert.That(((Decimal128Type)schema.GetFieldByIndex(1).DataType).Scale, Is.EqualTo(2));
        Assert.That(schema.GetFieldByIndex(2).IsNullable, Is.True);
        Assert.That(schema.GetFieldByIndex(2).Metadata["packata.required"], Is.EqualTo("true"));
        Assert.That(schema.GetFieldByIndex(7).DataType, Is.TypeOf<StructType>());
        Assert.That(schema.GetFieldByIndex(8).DataType, Is.TypeOf<ListType>());
        Assert.That(stream.Position, Is.EqualTo(stream.Length), "Exactly one message, with no EOS marker or data.");
        Assert.That(await reader.ReadNextRecordBatchAsync(), Is.Null);
    }

    [Test]
    public async Task Pyarrow_reads_the_message_and_matches_generated_Python_schema()
    {
        var python = Environment.GetEnvironmentVariable("PACKATA_TEST_PYTHON");
        if (python is null) { Assert.Ignore("Set PACKATA_TEST_PYTHON with PyArrow 23+."); return; }
        var result = await SchemaDocumentService.ExportAsync(ContractDocument.Parse(SchemaGenerationTests.Descriptor), new("arrow-ipc"));
        var generated = await new SchemaGenerationService().GenerateAsync(SchemaGenerationTests.Descriptor, new(Target: "arrow", Language: "python", TypeName: "Orders"));
        var path = Path.Combine(Path.GetTempPath(), "packata-schema-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(path + ".ipc", result.Bytes!);
            await File.WriteAllTextAsync(path + ".py", generated.Content + "\nwith open(__file__[:-3] + '.ipc', 'rb') as source:\n    schema = pa.ipc.read_schema(source)\n    assert schema.equals(Orders, check_metadata=True)\n    assert not source.read(), 'Expected one encapsulated schema message'\n    source.seek(0)\n    assert pa.ipc.read_message(source).type == 'schema'\n    assert not source.read()\n");
            var start = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add(path + ".py");
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.That(process.ExitCode, Is.Zero, await error);
        }
        finally { File.Delete(path + ".ipc"); File.Delete(path + ".py"); }
    }

    [Test]
    public async Task Selection_mapping_and_profile_errors_have_no_artifact()
    {
        var document = ContractDocument.Parse(SchemaGenerationTests.Descriptor);
        foreach (var options in new[] { new SchemaDocumentOptions("arrow-ipc", "missing"), new("arrow-ipc", Profile: "lance"), new("arrow") })
            Assert.That((await SchemaDocumentService.ExportAsync(document, options)).Bytes, Is.Null);
        var unsupported = ContractDocument.Parse(SchemaGenerationTests.Descriptor.Replace("precision: 18", "precision: 90"));
        var result = await SchemaDocumentService.ExportAsync(unsupported, new("arrow-ipc"));
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Bytes, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "ARROW_TYPE"), Is.True);
        var multiple = ContractDocument.Parse(SchemaGenerationTests.Descriptor + "\n  - name: another\n    properties:\n      - name: id\n        logicalType: string\n");
        Assert.That((await SchemaDocumentService.ExportAsync(multiple, new("arrow-ipc"))).Diagnostics[0].Code, Is.EqualTo("EXPORT_ASSET"));
        Assert.That((await SchemaDocumentService.ExportAsync(multiple, new("arrow-ipc", "orders"))).IsSuccessful, Is.True);
    }

    [Test]
    public void Cancelled_export_produces_no_artifact()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await SchemaDocumentService.ExportAsync(ContractDocument.Parse(SchemaGenerationTests.Descriptor), new("arrow-ipc"), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Canonical_metadata_and_explicit_map_survive_serialization()
    {
        var field = new Packata.Core.Contracts.DataField("map", "object", Children:
            [new("key", "string"), new("value", "integer", Nullable: true)],
            Extensions: Packata.Core.Contracts.ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["type"] = "map", ["note"] = "quoted\"\\\n" }));
        var mapping = Packata.Arrow.ArrowSchemaProjection.Map(new([field], Extensions:
            Packata.Core.Contracts.ExtensionMetadata.For("arrow", new Dictionary<string, object?> { ["owner"] = "analytics" })));
        var bytes = await Packata.Arrow.ArrowSchemaSerializer.SerializeAsync(mapping.Schema!);
        using var stream = new MemoryStream(bytes);
        using var reader = new ArrowStreamReader(stream);
        var schema = await reader.GetSchema();
        Assert.That(schema.Metadata["owner"], Is.EqualTo("analytics"));
        Assert.That(schema.GetFieldByIndex(0).Metadata["note"], Is.EqualTo("quoted\"\\\n"));
        var map = (MapType)schema.GetFieldByIndex(0).DataType;
        Assert.That(map.KeyField.IsNullable, Is.False);
        Assert.That(map.ValueField.IsNullable, Is.True);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public async Task Empty_declared_schema_remains_an_encapsulated_schema_message()
    {
        var result = await SchemaDocumentService.ExportAsync(ContractDocument.Parse("""{"resources":[{"name":"empty","schema":{"fields":[]}}]}"""), new("arrow-ipc"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        using var stream = new MemoryStream(result.Bytes!);
        using var reader = new ArrowStreamReader(stream);
        Assert.That((await reader.GetSchema()).FieldsList, Is.Empty);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }
}
