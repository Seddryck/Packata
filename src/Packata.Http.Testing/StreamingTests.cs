using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Http.Arrow;

namespace Packata.Http.Testing;

public class StreamingTests
{
    private static IAssetRepresentation Encoder(string format) => format switch
    {
        "ndjson" => new NdjsonRepresentation(), "avro" => new AvroRepresentation(), _ => new ArrowRepresentation()
    };

    [TestCase("ndjson")]
    [TestCase("avro")]
    [TestCase("arrow")]
    public async Task SlowDestinationBackpressuresLazyLargeSourceAndCancellationDisposesReader(string format)
    {
        var asset = HttpHostTests.Asset();
        using var cancellation = new CancellationTokenSource();
        var source = new TestReader(HttpHostTests.Schema.Fields.Select(field => field.Name).ToArray(), 100_000, HttpHostTests.Row);
        using var sink = new BackpressureStream(() => source.ReadCalls >= 2);
        var writing = Encoder(format).WriteAsync(asset, new TestFactory((_, _) => source), sink,
            new() { BatchSize = 2, MaxBufferedBytes = 4096 }, cancellation.Token);
        await sink.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(source.ReadCalls, Is.LessThanOrEqualTo(3));
        var reads = source.ReadCalls;
        await Task.Delay(50);
        Assert.That(source.ReadCalls, Is.EqualTo(reads));
        Assert.That(sink.MaximumWrite, Is.LessThanOrEqualTo(4096));
        await cancellation.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(() => writing);
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(sink.CanWrite, Is.True);
    }

    [TestCase("ndjson")]
    [TestCase("avro")]
    [TestCase("arrow")]
    public async Task ByteLimitsBoundWritesOverLargeInputs(string format)
    {
        var schema = new DataSchema([new("value", "string")]);
        var asset = HttpHostTests.Asset(schema: schema);
        var source = new TestReader(["value"], 10_000, i => [new string((char)('a' + (i % 26)), 100)]);
        using var sink = new BackpressureStream(() => false);
        await Encoder(format).WriteAsync(asset, new TestFactory((_, _) => source), sink,
            new() { BatchSize = 1000, MaxBufferedBytes = 4096 }, default);
        Assert.That(source.ReadCalls, Is.EqualTo(10_001));
        Assert.That(sink.MaximumWrite, Is.LessThanOrEqualTo(4096));
        Assert.That(sink.Writes, Is.GreaterThan(100));
        Assert.That(source.Disposals, Is.EqualTo(1));
    }

    [TestCase("ndjson", "precision")]
    [TestCase("avro", "precision")]
    [TestCase("ndjson", "scale")]
    [TestCase("avro", "scale")]
    [TestCase("ndjson", "timestamp")]
    [TestCase("avro", "timestamp")]
    [TestCase("ndjson", "null")]
    [TestCase("avro", "null")]
    [TestCase("ndjson", "infinity")]
    [TestCase("avro", "infinity")]
    [TestCase("ndjson", "row-limit")]
    [TestCase("avro", "row-limit")]
    public async Task RejectsLossyOrOversizeValuesAndDisposesSource(string format, string failure)
    {
        var (field, value) = failure switch
        {
            "precision" => (new DataField("value", "decimal", Constraints: [new("precision", 4), new("scale", 2)]), (object?)123.45m),
            "scale" => (new DataField("value", "decimal", Constraints: [new("precision", 4), new("scale", 2)]), 1.234m),
            "timestamp" => (new DataField("value", "timestamp"), DateTime.UnixEpoch.AddTicks(1)),
            "null" => (new DataField("value", "integer"), null),
            "infinity" => (new DataField("value", "number"), double.PositiveInfinity),
            _ => (new DataField("value", "string"), new string('x', 5000))
        };
        var source = new TestReader(["value"], 1, _ => [value]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<AssetEncodingException>(() => Encoder(format).WriteAsync(
            HttpHostTests.Asset(schema: new([field])), new TestFactory((_, _) => source), output,
            new() { MaxBufferedBytes = 4096 }, default));
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(output.CanWrite, Is.True);
    }

    [Test]
    public void UnsupportedMappingsAreAdvertisedPerRepresentation()
    {
        var unsigned = HttpHostTests.Asset(schema: new([new("value", "integer", Constraints: [new("signed", false)])]));
        Assert.That(new NdjsonRepresentation().Describe(unsigned).Supported, Is.True);
        Assert.That(new ArrowRepresentation().Describe(unsigned).Supported, Is.True);
        Assert.Throws<NotSupportedException>(() => new AvroRepresentation().Describe(unsigned));
        var nanos = HttpHostTests.Asset(schema: new([new("value", "timestamp", Constraints: [new("precision", "nanosecond")])]));
        Assert.Throws<NotSupportedException>(() => new AvroRepresentation().Describe(nanos));
        var names = HttpHostTests.Asset(schema: new([new("a b", "string")]));
        Assert.That(new NdjsonRepresentation().Describe(names).Supported, Is.True);
        Assert.Throws<NotSupportedException>(() => new AvroRepresentation().Describe(names));
    }

    [TestCase("ndjson")]
    [TestCase("avro")]
    [TestCase("arrow")]
    public async Task SourcePhysicalNameDoesNotChangeOutputColumnName(string format)
    {
        var asset = HttpHostTests.Asset(schema: new([new("logical", "integer", PhysicalName: "physical")]));
        var source = new TestReader(["physical"], 1, _ => [42]);
        using var output = new MemoryStream();
        await Encoder(format).WriteAsync(asset, new TestFactory((_, _) => source), output, new(), default);
        Assert.That(source.Disposals, Is.EqualTo(1));
        if (format == "ndjson")
        {
            using var row = JsonDocument.Parse(output.ToArray());
            Assert.That(row.RootElement.GetProperty("logical").GetString(), Is.EqualTo("42"));
        }
    }

    [Test]
    public async Task ShutdownCancelsActiveReadersAndStopsListener()
    {
        using var entered = new ManualResetEventSlim();
        TestReader? source = null;
        var asset = HttpHostTests.Asset();
        var factory = new TestFactory((_, token) => source = new(HttpHostTests.Schema.Fields.Select(field => field.Name).ToArray(), 100, i =>
        {
            if (i >= 2) { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            return HttpHostTests.Row(i);
        }));
        var host = await AssetHttpHost.StartAsync([asset], factory, HttpHostTests.Encoders,
            new() { Streaming = new() { BatchSize = 2 } });
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        using var response = await http.GetAsync($"assets/{asset.Id}/data", HttpCompletionOption.ResponseHeadersRead);
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        await host.DisposeAsync();
        Assert.That(source!.Disposals, Is.EqualTo(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("assets"));
    }

    [Test]
    public async Task TransferRegistryIsBoundedAndDoesNotEvictCompletionBeforeRetention()
    {
        var asset = HttpHostTests.Asset(0);
        await using var host = await AssetHttpHost.StartAsync([asset], new TestFactory((_, _) => new TestReader(
            HttpHostTests.Schema.Fields.Select(field => field.Name).ToArray(), 0, HttpHostTests.Row)), HttpHostTests.Encoders,
            new() { MaxRetainedTransfers = 1 });
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        using var first = await http.GetAsync($"assets/{asset.Id}/data");
        using var second = await http.GetAsync($"assets/{asset.Id}/data");
        Assert.That((int)second.StatusCode, Is.EqualTo(429));
        Assert.That((await http.GetFromJsonAsync<TransferStatus>(first.Headers.GetValues("Packata-Completion").Single()))!.State,
            Is.EqualTo("complete"));
    }

    private sealed class BackpressureStream(Func<bool> block) : Stream
    {
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumWrite { get; private set; }
        public int Writes { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaximumWrite = Math.Max(MaximumWrite, buffer.Length); Writes++;
            if (block()) { Blocked.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
