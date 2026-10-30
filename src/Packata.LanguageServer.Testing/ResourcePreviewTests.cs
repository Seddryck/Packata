using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Reading;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

[TestFixture]
public sealed class ResourcePreviewTests
{
    private static readonly Uri DocumentUri = new("file:///workspace/nested/datapackage.yaml");
    private static string Package(string path, string extra = "") => $"resources:\n  - name: orders\n    path: '{path}'\n{extra}";
    private static DocumentSnapshot Snapshot(string text, Uri? uri = null) => new(uri ?? DocumentUri, text, 1);
    private static Task<PreviewResult> ReadAsync(string text, IEndpointStreamResolver streams, int limit = 20,
        CancellationToken token = default, Uri? uri = null, string source = "/resources/0", int? binding = null)
        => ResourcePreview.ReadAsync(Snapshot(text, uri), source, limit, binding, token, streams);

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(20)]
    public async Task SelectedLimitStopsBeforeParsingTheNextDataRowAndDisposesAsync(int limit)
    {
        var data = "id\n" + string.Join("\n", Enumerable.Range(1, limit)) + "\nnot-an-integer\n";
        var streams = new TrackingResolver(data);
        var result = await ReadAsync(Package("../data/orders.csv", "    schema:\n      fields:\n        - name: id\n          type: integer\n"), streams, limit);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(limit));
        Assert.That(result.Columns.Single().Type, Is.EqualTo("integer"));
        Assert.That(streams.LastPath, Is.EqualTo(new Uri(DocumentUri, "../data/orders.csv").LocalPath));
        Assert.That(streams.Stream!.Disposed, Is.True);
    }

    [TestCase("id\n1\n2\n", 2)]
    [TestCase("id\n", 0)]
    [TestCase("", 0)]
    public async Task ShortHeaderOnlyAndEmptyFilesAsync(string data, int count)
    {
        var streams = new TrackingResolver(data);
        var result = await ReadAsync(Package("orders.csv", "    schema:\n      fields:\n        - name: id\n          type: integer\n"), streams);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(count));
        Assert.That(result.Columns.Single().Name, Is.EqualTo("id"));
        Assert.That(streams.Stream!.Disposed, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task InvalidLimitsDoNotAccessEvenTheDescriptorAsync(int limit)
    {
        var service = new ContractService(new DocumentStore());
        var result = await service.PreviewAsync(new Uri("file:///missing/descriptor.yaml"), "/resources/0", CancellationToken.None, limit);
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_LIMIT"));
    }

    [TestCase("s3://bucket/orders.csv", "")]
    [TestCase("az://bucket/orders.csv", "")]
    [TestCase("ftp://host/orders.csv", "")]
    [TestCase("https://user:secret@host/orders.csv", "")]
    [TestCase("https://host/orders.json", "")]
    [TestCase("https://host/orders.parquet", "")]
    [TestCase("https://host/orders.csv", "    headers:\n      Authorization: secret\n")]
    [TestCase("https://host/orders.csv", "    authentication:\n      type: bearer\n")]
    [TestCase("https://host/orders.csv", "    customProperties:\n      - property: request-headers\n        value: private-header\n")]
    public async Task UnsupportedTargetsAreInactiveAndDirectInvocationNeverAccessesDataAsync(string path, string extra)
    {
        var text = Package(path, extra);
        var store = new DocumentStore(); store.Update(DocumentUri, text, 1);
        var inspection = await new ContractService(store).InspectAsync(DocumentUri, CancellationToken.None);
        Assert.That(inspection.PreviewTargets!.Single().Supported, Is.False);
        var streams = new TrackingResolver("should not read");
        var result = await ReadAsync(text, streams);
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_UNSUPPORTED"));
        Assert.That(streams.Opens, Is.Zero);
        Assert.That(JsonConvert.SerializeObject(result), Does.Not.Contain("secret"));
    }

    [Test]
    public async Task DatabaseEndpointsAreRejectedBeforeAccessAsync()
    {
        var text = ContractServiceTests.Odcs + "\nservers:\n  - server: database\n    type: postgresql\n    host: localhost\n    port: 5432\n    database: secret\n    schema: public\n";
        var streams = new TrackingResolver("");
        var result = await ReadAsync(text, streams, source: "/schema/0");
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_UNSUPPORTED"));
        Assert.That(streams.Opens, Is.Zero);
    }

    [Test]
    public async Task MultipleOdcsServersRequireUnambiguousBindingAsync()
    {
        var text = ContractServiceTests.Odcs + "\nservers:\n  - server: first\n    type: local\n    path: first.csv\n  - server: second\n    type: local\n    path: second.csv\n";
        var streams = new TrackingResolver("");
        var result = await ReadAsync(text, streams, source: "/schema/0");
        Assert.That(result.Diagnostic!.Message, Does.Contain("unambiguous"));
        Assert.That(streams.Opens, Is.Zero);
    }

    [Test]
    public async Task SelectionUsesExactSourceIndexAndRejectsStaleBindingAsync()
    {
        var text = Package("wrong.csv") + "  - name: correct\n    path: correct.csv\n";
        var streams = new TrackingResolver("id\n7\n");
        var result = await ReadAsync(text, streams, source: "/resources/1", binding: 0);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(streams.LastPath, Does.EndWith("correct.csv"));
        var wrong = await ReadAsync(text, streams, source: "/resources/1", binding: 99);
        Assert.That(wrong.Diagnostic!.Code, Is.EqualTo("PREVIEW_SELECTION"));
        Assert.That(streams.Opens, Is.EqualTo(1));
    }

    [Test]
    public async Task DuplicateEndpointNamesAreDiagnosedWithoutChoosingAnotherResourceAsync()
    {
        var streams = new TrackingResolver("id\n1\n");
        var result = await ReadAsync(Package("first.csv") + "  - name: orders\n    path: second.csv\n", streams, source: "/resources/1");
        Assert.That(result.Diagnostic!.Message, Does.Contain("uniquely"));
        Assert.That(streams.Opens, Is.Zero);
    }

    [Test]
    public async Task AuthRelatedColumnNamesDoNotDisablePreviewAsync()
    {
        var streams = new TrackingResolver("headers\nvalue\n");
        var result = await ReadAsync(Package("data.csv", "    schema:\n      fields:\n        - name: headers\n          type: string\n"), streams, 1);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task JsonDescriptorsAndExplicitLineTerminatorsReuseReaderSettingsAsync()
    {
        var text = """{"resources":[{"name":"orders","path":"orders.csv","dialect":{"delimiter":";","lineTerminator":"\n"},"schema":{"fields":[{"name":"id","type":"integer"},{"name":"amount","type":"number"}]}}]}""";
        var streams = new BinaryResolver("id;amount\n1;2.5\n2;3.5\n"u8.ToArray());
        var result = await ReadAsync(text, streams, 1);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(1));
        Assert.That(result.Columns.Select(column => column.Name), Is.EqualTo(new[] { "id", "amount" }));
        Assert.That(streams.Stream.Disposed, Is.True);
    }

    [Test]
    public async Task DefaultLimitAndLocalOdcsDirectoryUseOriginalDocumentLocationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "packata-preview-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "data"));
        try
        {
            File.WriteAllText(Path.Combine(root, "data", "orders.csv"), "id\r\n" + string.Join("\r\n", Enumerable.Range(1, 25)));
            var uri = new Uri(Path.Combine(root, "contract.yaml"));
            var text = ContractServiceTests.Odcs.Replace("physicalType: table", "physicalType: file\n    physicalName: orders.csv")
                .Replace("logicalType: integer", "logicalType: string") + "\nservers:\n  - server: files\n    type: local\n    path: data/\n    format: csv\n";
            // Use a single-column schema matching the file.
            text = text[..text.IndexOf("      - name: address", StringComparison.Ordinal)] + "servers:\n  - server: files\n    type: local\n    path: data/\n    format: csv\n";
            var store = new DocumentStore(); store.Update(uri, text, 1);
            var service = new ContractService(store);
            var inspection = await service.InspectAsync(uri, CancellationToken.None);
            Assert.That(inspection.PreviewTargets!.Single().Supported, Is.True);
            var result = await service.PreviewAsync(uri, "/schema/0", CancellationToken.None);
            Assert.That(result.Diagnostic, Is.Null);
            Assert.That(result.Limit, Is.EqualTo(20));
            Assert.That(result.Rows, Has.Count.EqualTo(20));
            using var exclusive = File.Open(Path.Combine(root, "data", "orders.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HttpAndPreSignedUrlsUseExistingTransportWithoutAuthenticationAsync(bool odcs)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/folder/";
        var served = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var network = client.GetStream();
            using var input = new StreamReader(network, Encoding.ASCII, leaveOpen: true);
            var first = await input.ReadLineAsync();
            var headers = new List<string>();
            for (var line = await input.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await input.ReadLineAsync()) headers.Add(line);
            Assert.That(first, Does.Contain("/folder/data.csv?sig=private-signature"));
            Assert.That(headers.Any(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)), Is.False);
            await network.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 13\r\nConnection: close\r\n\r\nid\r\n1\r\n2\r\n3\r\n"u8.ToArray());
        });
        var text = odcs ? """
            kind: DataContract
            apiVersion: v3.2.0
            id: remote
            version: 1
            status: active
            schema:
              - name: orders
            servers:
              - server: public
                type: api
                location: data.csv?sig=private-signature
            """ : Package("data.csv?sig=private-signature", "    format: csv\n");
        var uri = new Uri(origin + "contract.yaml");
        var store = new DocumentStore(); store.Update(uri, text, 1);
        var service = new ContractService(store);
        var inspection = await service.InspectAsync(uri, CancellationToken.None);
        Assert.That(inspection.PreviewTargets!.Single().Supported, Is.True);
        Assert.That(served.IsCompleted, Is.False, "Inspection must not initiate access.");
        var result = await service.PreviewAsync(uri, odcs ? "/schema/0" : "/resources/0", CancellationToken.None, 2);
        await served.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ParsingFailureDisposesAndSanitizesAsync()
    {
        var streams = new TrackingResolver("id\nprivate-data\n");
        var result = await ReadAsync(Package("https://host/secret.csv?sig=private-signature", "    schema:\n      fields:\n        - name: id\n          type: integer\n"), streams);
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_PARSE"));
        Assert.That(JsonConvert.SerializeObject(result), Does.Not.Contain("private-data").And.Not.Contain("private-signature").And.Not.Contain("secret.csv"));
        Assert.That(streams.Stream!.Disposed, Is.True);
    }

    [TestCase("csv")]
    [TestCase("xlsx")]
    public async Task CancellationDuringReadOrStagingDisposesStreamAsync(string format)
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "packata-stage-*");
        using var cancellation = new CancellationTokenSource();
        var streams = new TrackingResolver("", stall: true);
        var operation = Task.Run(() => ReadAsync(Package("data." + format), streams, token: cancellation.Token));
        await streams.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await Assert.ThatAsync(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(streams.Stream!.Disposed, Is.True);
        Assert.That(Directory.GetFiles(Path.GetTempPath(), "packata-stage-*"), Is.EquivalentTo(before));
    }

    [Test]
    public async Task SpreadsheetParsingFailureReleasesStagingAndSanitizesAsync()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "packata-stage-*");
        var streams = new TrackingResolver("private-content");
        var result = await ReadAsync(Package("https://host/private.xlsx?sig=private-signature"), streams);
        Assert.That(result.Diagnostic, Is.Not.Null);
        Assert.That(JsonConvert.SerializeObject(result), Does.Not.Contain("private-content").And.Not.Contain("private-signature"));
        Assert.That(streams.Stream!.Disposed, Is.True);
        Assert.That(Directory.GetFiles(Path.GetTempPath(), "packata-stage-*"), Is.EquivalentTo(before));
    }

    [TestCase("my-book.xlsx", 1)]
    public async Task BinaryFilesAreStagedAndLimitedAsync(string resource, int limit)
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", resource));
        var streams = new BinaryResolver(bytes);
        var result = await ReadAsync(Package("https://host/" + resource + "?sig=private-signature"), streams, limit);
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Rows, Has.Count.EqualTo(limit));
        Assert.That(result.Columns, Is.Not.Empty);
        Assert.That(result.Columns.All(column => column.Type != "unknown"), Is.True);
        Assert.That(streams.Stream.Disposed, Is.True);
    }

    [Test]
    public async Task StaleSelectionKeyAndPreCanceledOperationNeverAccessDataAsync()
    {
        var store = new DocumentStore(); store.Update(DocumentUri, Package("original.csv"), 1);
        var service = new ContractService(store);
        var selected = (await service.InspectAsync(DocumentUri, CancellationToken.None)).PreviewTargets!.Single();
        var streams = new TrackingResolver("id\n1\n");
        var result = await ResourcePreview.ReadAsync(Snapshot(Package("different.csv")), "/resources/0", 20, 0,
            CancellationToken.None, streams, selected.SelectionKey);
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_SELECTION"));
        using var canceled = new CancellationTokenSource(); await canceled.CancelAsync();
        await Assert.ThatAsync(() => ReadAsync(Package("original.csv"), streams, token: canceled.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(streams.Opens, Is.Zero);
    }

    [Test]
    public async Task AccessFailureIsSanitizedAsync()
    {
        var result = await ReadAsync(Package("https://host/private.csv?sig=private-signature"), new FailingResolver());
        Assert.That(result.Diagnostic!.Code, Is.EqualTo("PREVIEW_ACCESS"));
        Assert.That(JsonConvert.SerializeObject(result), Does.Not.Contain("private-signature").And.Not.Contain("private.csv"));
    }

    private sealed class FailingResolver : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("https://host/private.csv?sig=private-signature");
    }
    private sealed class BinaryResolver(byte[] bytes) : IEndpointStreamResolver
    {
        public TrackedStream Stream { get; } = new(bytes, false, new());
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult<Stream>(Stream);
    }

    private sealed class TrackingResolver(string data, bool stall = false) : IEndpointStreamResolver
    {
        public int Opens { get; private set; }
        public string? LastPath { get; private set; }
        public TrackedStream? Stream { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            Opens++; LastPath = path;
            Stream = new TrackedStream(Encoding.UTF8.GetBytes(data.ReplaceLineEndings("\r\n")), stall, Started);
            return ValueTask.FromResult<Stream>(Stream);
        }
    }
    private sealed class TrackedStream(byte[] data, bool stall, TaskCompletionSource started) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            started.TrySetResult();
            if (stall) await Task.Delay(Timeout.Infinite, token);
            return await base.ReadAsync(buffer, token);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

