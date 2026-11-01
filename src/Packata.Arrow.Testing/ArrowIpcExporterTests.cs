using System.Data;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders;
using static Packata.Arrow.Testing.ArrowRecordBatchesTests;

namespace Packata.Arrow.Testing;

public class ArrowIpcExporterTests
{
    private static readonly DataSchema Schema = new([
        new("id", "integer"),
        new("text", "string", Nullable: true),
        new("amount", "number", Constraints: [new("precision", 8), new("scale", 2)], Nullable: true),
        new("instant", "timestamp", Nullable: true)]);
    private static readonly DateTimeOffset Instant = new(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(2));
    private static DataContract Contract(int count = 5) => new(new("contract"), new(),
        [new("asset", "logical", "physical", null, AssetKind.Table, Schema,
            [new("unselected"), new("source", "selected-path")])],
        [new("source", null, EndpointKind.Inline, null, new InlineLocation(
            Enumerable.Range(0, count).Select(Row)))], new());
    private static object?[] Row(int i) => [i, i == 1 ? null : "héllo", i == 1 ? null : -123.45m,
        i == 1 ? null : Instant];

    [TestCase(0)]
    [TestCase(5)]
    public async Task ExportsInteroperableSchemaAndValuesIncludingEmptyInput(int count)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "arrow-interop", $"rows-{count}.arrows");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // WriteFileAsync intentionally refuses to replace existing caller data.
        if (File.Exists(path)) File.Delete(path);
        await new ArrowIpcExporter(new ResourceReaderFactory()).WriteFileAsync(Contract(count), "asset", 1, path,
            new() { BatchSize = 2 });
        using var stream = File.OpenRead(path);
        using var reader = new ArrowStreamReader(stream);
        var schema = await reader.GetSchema();
        Assert.That(schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "id", "text", "amount", "instant" }));
        Assert.That(schema.GetFieldByIndex(0).IsNullable, Is.False);
        Assert.That(schema.GetFieldByIndex(1).IsNullable, Is.True);
        var total = 0;
        var sizes = new List<int>();
        while (await reader.ReadNextRecordBatchAsync() is { } batch)
        {
            using (batch)
            {
                sizes.Add(batch.Length);
                for (var i = 0; i < batch.Length; i++, total++)
                {
                    Assert.That(((Int64Array)batch.Column(0)).GetValue(i), Is.EqualTo(total));
                    if (total == 1)
                    {
                        for (var column = 1; column < 4; column++) Assert.That(batch.Column(column).IsNull(i), Is.True);
                    }
                    else
                    {
                        Assert.That(((StringArray)batch.Column(1)).GetString(i), Is.EqualTo("héllo"));
                        Assert.That(((Decimal128Array)batch.Column(2)).GetValue(i), Is.EqualTo(-123.45m));
                        Assert.That(((TimestampArray)batch.Column(3)).GetTimestamp(i), Is.EqualTo(Instant.ToUniversalTime()));
                    }
                }
            }
        }
        Assert.That(total, Is.EqualTo(count));
        Assert.That(sizes, Is.EqualTo(count == 0 ? System.Array.Empty<int>() : [2, 2, 1]));
        await Assert.ThrowsAsync<IOException>(() => new ArrowIpcExporter(new ResourceReaderFactory())
            .WriteFileAsync(Contract(count), "asset", 1, path));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ResolvesBindingAndHonorsDestinationOwnership(bool leaveOpen)
    {
        var source = new RowsReader(["id", "text", "amount", "instant"], 5, Row);
        var factory = new Factory(source);
        using var output = new MemoryStream();
        var contract = Contract();
        await new ArrowIpcExporter(factory).WriteAsync(contract, "asset", 1, output, new() { LeaveOpen = leaveOpen });
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(output.CanWrite, Is.EqualTo(leaveOpen));
        Assert.That(factory.Request!.Endpoint.Id, Is.EqualTo("source"));
        Assert.That(factory.Request.AssetPath, Is.EqualTo("selected-path"));
        Assert.That(factory.Request.Contract, Is.SameAs(contract));
        Assert.That(factory.Request.Schema, Is.SameAs(Schema));
    }

    [TestCase("unknown-asset")]
    [TestCase("duplicate-asset")]
    [TestCase("missing-schema")]
    [TestCase("missing-endpoint")]
    [TestCase("duplicate-endpoint")]
    [TestCase("binding")]
    [TestCase("options")]
    [TestCase("unsupported-schema")]
    public async Task InvalidSelectionDoesNotOpenSourceOrTakeDestination(string failure)
    {
        var contract = Contract();
        contract = failure switch
        {
            "duplicate-asset" => contract with { Assets = [contract.Assets[0], contract.Assets[0]] },
            "missing-schema" => contract with { Assets = [contract.Assets[0] with { Schema = null }] },
            "missing-endpoint" => contract with { Endpoints = [] },
            "duplicate-endpoint" => contract with { Endpoints = [contract.Endpoints[0], contract.Endpoints[0]] },
            "unsupported-schema" => contract with { Assets = [contract.Assets[0] with { Schema = new([new("x", "object")]) }] },
            _ => contract
        };
        var factory = new Factory();
        using var output = new MemoryStream();
        await Assert.CatchAsync<Exception>(() => new ArrowIpcExporter(factory).WriteAsync(contract,
            failure == "unknown-asset" ? "missing" : "asset", failure == "binding" ? 2 : 1, output,
            new() { LeaveOpen = false, BatchSize = failure == "options" ? 0 : 2 }));
        Assert.That(factory.Request, Is.Null);
        Assert.That(output.CanWrite, Is.True);
        Assert.That(output.Length, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceOpenFailureHonorsOwnership(bool leaveOpen)
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => new ArrowIpcExporter(new Factory()).WriteAsync(Contract(), "asset", 1,
            output, new() { LeaveOpen = leaveOpen }));
        Assert.That(output.CanWrite, Is.EqualTo(leaveOpen));
    }

    [TestCase("read", false)]
    [TestCase("conversion", false)]
    [TestCase("write", false)]
    [TestCase("flush", false)]
    [TestCase("cancel", false)]
    [TestCase("read", true)]
    [TestCase("conversion", true)]
    [TestCase("write", true)]
    [TestCase("flush", true)]
    [TestCase("cancel", true)]
    public async Task FailuresDisposeSourceAndHonorDestinationOwnership(string failure, bool leaveOpen)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RowsReader(["id", "text", "amount", "instant"], 100, i =>
        {
            if (failure == "read") throw new IOException("Source failed.");
            if (failure == "cancel") cancellation.Cancel();
            return failure == "conversion" ? ["invalid", null, null, null] : Row(i);
        });
        using var output = new FailingStream
        {
            BeforeWrite = () =>
            {
                // Reject only after a batch is produced: the exporter must not read the entire source first.
                if (failure == "write" && source.ReadCalls >= 2)
                {
                    Assert.That(source.ReadCalls, Is.EqualTo(2));
                    throw new IOException("Destination failed.");
                }
            },
            FailFlush = failure == "flush"
        };
        var error = await Assert.CatchAsync<Exception>(() => new ArrowIpcExporter(new Factory(source)).WriteAsync(
            Contract(), "asset", 1, output, new() { BatchSize = 2, LeaveOpen = leaveOpen }, cancellation.Token));
        Assert.That(error, failure == "cancel" ? Is.InstanceOf<OperationCanceledException>() :
            failure == "conversion" ? Is.InstanceOf<InvalidDataException>() : Is.InstanceOf<IOException>());
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(output.CanWrite, Is.EqualTo(leaveOpen));
    }

    [Test]
    public async Task PreCancellationDoesNotOpenSource()
    {
        var factory = new Factory();
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ArrowIpcExporter(factory).WriteAsync(
            Contract(), "asset", 1, output, cancellationToken: new(true)));
        Assert.That(factory.Request, Is.Null);
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public async Task ExportsToNonSeekableStream()
    {
        using var output = new FailingStream { Seekable = false };
        await new ArrowIpcExporter(new ResourceReaderFactory()).WriteAsync(Contract(), "asset", 1, output);
        using var input = new ArrowStreamReader(new MemoryStream(output.ToArray()));
        using var batch = await input.ReadNextRecordBatchAsync();
        Assert.That(batch!.Length, Is.EqualTo(5));
    }

    [Test]
    public async Task InvalidFileSelectionCreatesNoFile()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"invalid-{Guid.NewGuid()}.arrows");
        await Assert.ThrowsAsync<ArgumentException>(() => new ArrowIpcExporter(new Factory()).WriteFileAsync(
            Contract(), "missing", 1, path));
        Assert.That(File.Exists(path), Is.False);
    }

    private sealed class Factory(IDataReader? source = null) : IDataEndpointReaderFactory
    {
        public DataEndpointReadRequest? Request { get; private set; }
        public ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
            CancellationToken cancellationToken = default) => throw new AssertionException("Asset context was lost.");
        public ValueTask<IDataReader> OpenAsync(DataEndpointReadRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return source is null ? ValueTask.FromException<IDataReader>(new IOException("Open failed.")) : ValueTask.FromResult(source);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public Action? BeforeWrite { get; init; }
        public bool FailFlush { get; init; }
        public bool Seekable { get; init; } = true;
        public override bool CanSeek => Seekable;
        public override long Seek(long offset, SeekOrigin loc)
            => Seekable ? base.Seek(offset, loc) : throw new NotSupportedException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BeforeWrite?.Invoke();
            return base.WriteAsync(buffer, cancellationToken);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BeforeWrite?.Invoke();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
            => FailFlush ? Task.FromException(new IOException("Flush failed.")) : base.FlushAsync(cancellationToken);
    }
}

