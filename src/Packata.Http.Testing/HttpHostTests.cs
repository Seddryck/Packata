using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Http.Arrow;
using Packata.ResourceReaders;

namespace Packata.Http.Testing;

public class HttpHostTests
{
    internal static IAssetRepresentation[] Encoders => [new NdjsonRepresentation(), new AvroRepresentation(), new ArrowRepresentation()];
    internal static DataSchema Schema => new([new("id", "integer", Format: "i64"), new("text", "string", Nullable: true),
        new("amount", "number", Constraints: [new("precision", 18), new("scale", 2)], Nullable: true),
        new("instant", "timestamp", Nullable: true), new("day", "date"), new("bytes", "binary"),
        new("number", "number", Format: "fp64"), new("flag", "boolean")]);
    internal static object?[] Row(int i) => [9007199254740993L + i, i == 1 ? null : "héllo\n\"world\"",
        i == 1 ? null : -123.45m, i == 1 ? null : new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(2)),
        new DateOnly(1969, 12, 31), new byte[] { 0, 255 }, 1.5, true];
    internal static ServedAsset Asset(int count = 5, DataSchema? schema = null, string id = "data", string version = "1.0.0")
    {
        var contract = new DataContract(new("contract", Version: version), new(),
            [new(id, id, "physical", null, AssetKind.Table, schema ?? Schema, [new("source", "selected")])],
            [new("source", null, EndpointKind.Inline, null, new InlineLocation(Enumerable.Range(0, count).Select(Row)))], new());
        return new(contract, id, 0);
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestCase(null, "application/x-ndjson")]
    [TestCase("*/*", "application/x-ndjson")]
    [TestCase("application/*", "application/x-ndjson")]
    [TestCase("application/avro", "application/avro")]
    [TestCase("avro/binary", "application/avro")]
    [TestCase("application/ndjson", "application/x-ndjson")]
    [TestCase("application/avro;q=0.9,application/x-ndjson;q=0.2", "application/avro")]
    [TestCase("application/avro;q=0.8,application/x-ndjson;q=0.8", "application/x-ndjson")]
    [TestCase("application/x-ndjson;q=0,*/*;q=1", "application/avro")]
    [TestCase("application/x-ndjson;q=0,application/ndjson;q=1", null)]
    [TestCase("application/*;q=0,*/*;q=1", null)]
    [TestCase("application/x-ndjson;q=0.1,application/*;q=0.9", "application/avro")]
    [TestCase("application/avro,*/*", "application/avro")]
    [TestCase("application/vnd.apache.arrow.stream", "application/vnd.apache.arrow.stream")]
    [TestCase("text/csv", null)]
    [TestCase("*/*;q=0", null)]
    [TestCase("application/avro;q=2", null)]
    [TestCase("application/avro;q=invalid", null)]
    [TestCase("application/avro;q=0.1234", null)]
    [TestCase("application/x-ndjson;q=1,application/x-ndjson;charset=utf-8;q=0", null)]
    [TestCase("invalid", null)]
    [TestCase("application/avro;unknown=1", null)]
    public void NegotiatesQualitySpecificityWildcardsExclusionsAliasesAndPreferences(string? accept, string? expected)
        => Assert.That(AcceptNegotiation.Select(accept, Enumerable.Reverse(Encoders))?.MediaType, Is.EqualTo(expected));

    [Test]
    public async Task MetadataAndReadinessAreAvailableWithoutOpeningTheSource()
    {
        var asset = Asset(0);
        var factory = new TestFactory((_, _) => throw new IOException());
        await using var host = await AssetHttpHost.StartAsync([asset], factory, Encoders);
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        var metadata = await http.GetFromJsonAsync<ServedAssetMetadata[]> ("assets");
        Assert.That(metadata![0].ContractVersion, Is.EqualTo("1.0.0"));
        Assert.That(metadata[0].Formats.Count(format => format.Enabled), Is.EqualTo(3));
        Assert.That(metadata[0].Columns.Select(column => column.Name), Is.EqualTo(Schema.Fields.Select(field => field.Name)));
        Assert.That((await http.GetAsync(metadata[0].SchemaUrl)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(host.Readiness.BaseUrl, Is.EqualTo(host.BaseUri.AbsoluteUri));
        Assert.That(host.BaseUri.Port, Is.GreaterThan(0));
        Assert.That(factory.Opens, Is.Zero);
        Assert.That(Asset(version: "2.0.0").Id, Is.Not.EqualTo(asset.Id));
        Assert.That(Asset().Id, Is.EqualTo(asset.Id));
    }

    [Test]
    public async Task StartupRejectsDuplicateSelectionsAndOccupiedPortsWithoutOpeningSources()
    {
        var asset = Asset();
        var factory = new TestFactory((_, _) => throw new IOException());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServedAsset(asset.Contract, "data", 1));
        Assert.Throws<ArgumentException>(() => new ServedAsset(asset.Contract, "unknown", 0));
        await Assert.ThrowsAsync<ArgumentException>(() => AssetHttpHost.StartAsync([asset, asset], factory, Encoders));
        await using var host = await AssetHttpHost.StartAsync([asset], factory, Encoders);
        await Assert.CatchAsync<IOException>(() => AssetHttpHost.StartAsync([asset], factory, Encoders, new() { Port = host.BaseUri.Port }));
        Assert.That(factory.Opens, Is.Zero);
    }

    [TestCase("ndjson", 0)]
    [TestCase("ndjson", 5)]
    [TestCase("avro", 0)]
    [TestCase("avro", 5)]
    [TestCase("arrow", 0)]
    [TestCase("arrow", 5)]
    public async Task AllFormatsReturnMatchingIdentityAndVerifiedCompletion(string format, int count)
    {
        var asset = Asset(count);
        await using var host = await AssetHttpHost.StartAsync([asset], new ResourceReaderFactory(), Encoders,
            new() { Streaming = new() { BatchSize = 2 } });
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"assets/{asset.Id}/data");
        request.Headers.Accept.ParseAdd(Media(format));
        using var response = await http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo(Media(format)));
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var completion = await http.GetFromJsonAsync<TransferStatus>(response.Headers.GetValues("Packata-Completion").Single());
        Assert.That(completion!.State, Is.EqualTo("complete"));
        Assert.That(completion.AssetId, Is.EqualTo(asset.Id));
        Assert.That(completion.Bytes, Is.EqualTo(bytes.Length));
        Assert.That(completion.Sha256, Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        if (format == "ndjson")
        {
            var rows = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(rows.Length, Is.EqualTo(count));
            if (count > 0)
            {
                using var row = JsonDocument.Parse(rows[0]);
                Assert.That(row.RootElement.GetProperty("id").GetString(), Is.EqualTo("9007199254740993"));
                Assert.That(row.RootElement.GetProperty("amount").GetString(), Is.EqualTo("-123.45"));
            }
        }
        if (format == "arrow")
        {
            using var reader = new ArrowStreamReader(new MemoryStream(bytes));
            Assert.That((await reader.GetSchema()).FieldsList.Count, Is.EqualTo(8));
            var sizes = new List<int>();
            while (await reader.ReadNextRecordBatchAsync() is { } batch) { sizes.Add(batch.Length); batch.Dispose(); }
            Assert.That(sizes, Is.EqualTo(count == 0 ? System.Array.Empty<int>() : [2, 2, 1]));
        }
        if (format == "avro") Assert.That(bytes.Take(4), Is.EqualTo(new byte[] { 79, 98, 106, 1 }));
    }

    [TestCase("disabled")]
    [TestCase("mapping")]
    [TestCase("source")]
    public async Task UnavailableFormatsReturn406BeforeSourceAcquisition(string failure)
    {
        var asset = failure == "mapping" ? Asset(schema: new([new("x", "object")])) : Asset();
        var factory = new TestFactory((_, _) => throw new IOException());
        await using var host = await AssetHttpHost.StartAsync([asset], factory,
            failure == "disabled" ? [new NdjsonRepresentation()] : Encoders,
            new() { SourceLimitation = failure == "source" ? _ => "Source provider unavailable." : null });
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"assets/{asset.Id}/data");
        request.Headers.Accept.ParseAdd(Media("arrow"));
        using var response = await http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotAcceptable));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("formats"));
        Assert.That(factory.Opens, Is.Zero);
    }

    [TestCase("ndjson")]
    [TestCase("avro")]
    [TestCase("arrow")]
    public async Task InitialOutputArrivesBeforeSourceExhaustionAndFailureCannotComplete(string format)
    {
        using var gate = new ManualResetEventSlim();
        TestReader? source = null;
        var factory = new TestFactory((_, token) => source = new(Schema.Fields.Select(field => field.Name).ToArray(), 100, i =>
        {
            if (i == 2) { gate.Wait(token); throw new IOException("source secret must not be published"); }
            return Row(i);
        }));
        var asset = Asset();
        await using var host = await AssetHttpHost.StartAsync([asset], factory, Encoders,
            new() { Streaming = new() { BatchSize = 2 } });
        using var http = new HttpClient { BaseAddress = host.BaseUri, Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"assets/{asset.Id}/data");
        request.Headers.Accept.ParseAdd(Media(format));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var statusUrl = response.Headers.GetValues("Packata-Completion").Single();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[8192];
            Assert.That(await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5)), Is.GreaterThan(0));
            Assert.That(source!.ReadCalls, Is.LessThanOrEqualTo(3));
            Assert.That((await http.GetFromJsonAsync<TransferStatus>(statusUrl))!.State, Is.EqualTo("pending"));
            gate.Set();
            try { while (await stream.ReadAsync(buffer) > 0) { } } catch (IOException) { }
            await WaitUntil(() => source.Disposals == 1);
            Assert.That((await http.GetFromJsonAsync<TransferStatus>(statusUrl))!.State, Is.EqualTo("failed"));
        }
        finally { gate.Set(); }
    }

    [TestCase("ndjson")]
    [TestCase("avro")]
    [TestCase("arrow")]
    public async Task DisconnectCancelsSourceAndDisposesReader(string format)
    {
        TestReader? source = null;
        var factory = new TestFactory((_, token) => source = new(Schema.Fields.Select(field => field.Name).ToArray(), 100, i =>
        {
            if (i >= 2) token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return Row(i);
        }));
        var asset = Asset();
        await using var host = await AssetHttpHost.StartAsync([asset], factory, Encoders,
            new() { Streaming = new() { BatchSize = 2 } });
        using var http = new HttpClient { BaseAddress = host.BaseUri, Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"assets/{asset.Id}/data");
        request.Headers.Accept.ParseAdd(Media(format));
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var statusUrl = response.Headers.GetValues("Packata-Completion").Single();
        var stream = await response.Content.ReadAsStreamAsync();
        Assert.That(await stream.ReadAsync(new byte[16]), Is.GreaterThan(0));
        response.Dispose();
        await WaitUntil(() => source?.Disposals == 1);
        Assert.That((await http.GetFromJsonAsync<TransferStatus>(statusUrl))!.State, Is.EqualTo("failed"));
    }

    [Test]
    public async Task OpenFailureIsStructuredBeforeHeadersAndDoesNotExposeProviderDetails()
    {
        var asset = Asset();
        await using var host = await AssetHttpHost.StartAsync([asset], new TestFactory((_, _) => throw new IOException("secret")), Encoders);
        using var http = new HttpClient { BaseAddress = host.BaseUri };
        using var response = await http.GetAsync($"assets/{asset.Id}/data");
        Assert.That((int)response.StatusCode, Is.EqualTo(422));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("ASSET_READ_FAILED").And.Not.Contain("secret"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PythonReadersConsumeLiveResponsesIncrementally(bool packagedClient)
    {
        var python = Environment.GetEnvironmentVariable("PACKATA_INTEROP_PYTHON");
        if (python is null) Assert.Ignore("Set PACKATA_INTEROP_PYTHON to Python with pyarrow, fastavro and timezone data for live interoperability checks.");
        using var gate = new ManualResetEventSlim();
        TestReader? current = null;
        var opened = new List<TestReader>();
        var factory = new TestFactory((request, token) =>
        {
            gate.Reset();
            var count = request.Contract!.Assets[0].Id == "empty" ? 0 : 5;
            current = new(Schema.Fields.Select(field => field.Name).ToArray(), count, i =>
            {
                if (i == 2) gate.Wait(token);
                return Row(i);
            });
            opened.Add(current);
            return current;
        });
        await using var host = await AssetHttpHost.StartAsync([Asset(), Asset(0, id: "empty")], factory, Encoders,
            new() { Streaming = new() { BatchSize = 2 } });
        var start = new ProcessStartInfo(python!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(TestContext.CurrentContext.TestDirectory, "Interop",
            packagedClient ? "validate_client.py" : "validate_http.py"));
        start.ArgumentList.Add(host.BaseUri.AbsoluteUri);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                TestContext.Out.WriteLine(line);
                if (line.StartsWith("first ", StringComparison.Ordinal))
                {
                    Assert.That(current!.ReadCalls, Is.LessThanOrEqualTo(3), "Python must decode its first row/batch before the source completes.");
                    gate.Set();
                }
            }
            await process.WaitForExitAsync(timeout.Token);
        }
        catch { gate.Set(); process.Kill(entireProcessTree: true); throw; }
        Assert.That(process.ExitCode, Is.Zero, await stderr);
        Assert.That(opened.Count, Is.EqualTo(6));
        Assert.That(opened.All(reader => reader.Disposals == 1), Is.True);
    }

    internal static string Media(string format) => format switch { "ndjson" => "application/x-ndjson", "avro" => "application/avro", _ => "application/vnd.apache.arrow.stream" };
    internal static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
}
