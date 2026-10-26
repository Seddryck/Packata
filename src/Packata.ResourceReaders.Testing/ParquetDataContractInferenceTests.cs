using System.Buffers.Binary;
using System.Reflection;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.ResourceReaders.Parquet;
using Parquet;
using Parquet.Schema;

namespace Packata.ResourceReaders.Testing;

public sealed class ParquetDataContractInferenceTests
{
    [Test]
    public async Task Infer_ReadsOnlyMetadataAndRestoresCallerStream()
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Packata.ResourceReaders.Testing.Resources.iris.parquet")!;
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        using var stream = new MetadataOnlyStream(copy.ToArray()) { Position = 2 };
        var result = await new ParquetDataContractInference(new() { AssetId = "flowers", EndpointId = "source" })
            .InferAsync(stream, "iris.parquet");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Contract.Assets.Single().Schema!.Fields, Has.Count.EqualTo(5));
            Assert.That(result.Contract.Assets.Single().Schema!.Fields[4].LogicalType, Is.EqualTo("string"));
            Assert.That(result.Contract.Assets.Single().Id, Is.EqualTo("flowers"));
            Assert.That(result.Contract.Assets.Single().EndpointBindings.Single().EndpointId, Is.EqualTo("source"));
            Assert.That(stream.Position, Is.EqualTo(2));
            Assert.That(stream.CanRead, Is.True);
        });
    }

    [Test]
    public async Task Infer_EmptyFilePreservesDeclaredTypesNullabilityAndStructure()
    {
        var schema = new ParquetSchema(
            new global::Parquet.Schema.DataField<int>("id", false),
            new global::Parquet.Schema.DataField<string>("name"),
            new DecimalDataField("amount", 18, 4),
            new DateTimeDataField("day", DateTimeFormat.Date),
            new StructField("address", new global::Parquet.Schema.DataField<string>("city")),
            new ListField("tags", new global::Parquet.Schema.DataField<string>("element")));
        using var stream = new MemoryStream();
        await using (await ParquetWriter.CreateAsync(schema, stream)) { }
        var result = await new ParquetDataContractInference().InferAsync(stream, "empty.parquet");
        var fields = result.Contract.Assets.Single().Schema!.Fields;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(fields.Select(field => field.Name), Is.EqualTo(new[] { "id", "name", "amount", "day", "address", "tags" }));
            Assert.That(fields[0].Required, Is.True);
            Assert.That(fields[1].Required, Is.False);
            Assert.That(fields[2].PhysicalType, Does.Contain("DECIMAL(18,4)"));
            Assert.That(fields[3].LogicalType, Is.EqualTo("date"));
            Assert.That(fields[4].Children.Single().Name, Is.EqualTo("city"));
            Assert.That(fields[5].Items!.LogicalType, Is.EqualTo("string"));
        });
    }

    [Test]
    public async Task Infer_NullableColumnRemainsNullableEvenWithoutNullValues()
    {
        var field = new global::Parquet.Schema.DataField<int>("id", true);
        using var stream = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(new ParquetSchema(field), stream))
        {
            using var group = writer.CreateRowGroup();
            await group.WriteAsync<int>(field, new int?[] { 1, 2 }.AsMemory());
        }
        var result = await new ParquetDataContractInference().InferAsync(stream, "nullable.parquet");
        Assert.That(result.Contract.Assets.Single().Schema!.Fields.Single().Required, Is.False);
    }

    [Test]
    public async Task Infer_MapProducesErrorWithFieldPath()
    {
        using var stream = new MemoryStream();
        await using (await ParquetWriter.CreateAsync(new ParquetSchema(new MapField("lookup",
            new global::Parquet.Schema.DataField<string>("key", false), new global::Parquet.Schema.DataField<int>("value"))),
            stream)) { }
        var result = await new ParquetDataContractInference().InferAsync(stream, "map.parquet");
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("map.parquet/lookup"));
    }

    [Test]
    public async Task Infer_RejectsCorruptAndCancelledInput()
    {
        using var stream = new MemoryStream(new byte[20]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await new ParquetDataContractInference().InferAsync(stream, "bad.parquet"));
        Assert.That(stream.Position, Is.Zero);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new ParquetDataContractInference()
            .InferAsync(stream, "cancelled.parquet", new CancellationToken(true)));
    }

    [Test]
    public async Task Infer_EnforcesFooterLimit()
    {
        using var stream = new MemoryStream();
        await using (await ParquetWriter.CreateAsync(new ParquetSchema(new global::Parquet.Schema.DataField<int>("id")),
            stream)) { }
        await Assert.ThrowsAsync<InvalidDataException>(async () => await new ParquetDataContractInference(new() { MaximumFooterBytes = 1 })
            .InferAsync(stream, "large.parquet"));
    }

    private sealed class MetadataOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        private readonly long _footerStart = bytes.Length - 8 - BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8));
        private void Check(int count)
        {
            if (Position < _footerStart && Position + count > 4)
                throw new InvalidOperationException("Inference attempted to read row data.");
        }
        public override int Read(byte[] buffer, int offset, int count) { Check(count); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Check(buffer.Length); return base.Read(buffer); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Check(buffer.Length); return base.ReadAsync(buffer, cancellationToken); }
    }
}
