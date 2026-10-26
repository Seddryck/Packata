using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Reading;

namespace Packata.Cli.Testing;

[TestFixture("zip")]
[TestFixture("tar")]
[TestFixture("tar.gz")]
[TestFixture("tar.br")]
public sealed class ArchivePreviewTests(string suffix)
{
    [TestCase("datapackage.json", "data/customers.csv", "data/customers.csv")]
    [TestCase("package/datapackage.json", "data/customer names.csv", "package/data/customer names.csv")]
    [TestCase("package/datapackage.json", "../data/customers.csv", "data/customers.csv")]
    public async Task Preview_ResolvesEntriesRelativeToDescriptorAndSelectsAsset(string descriptor, string resource, string entry)
    {
        using var workspace = new CliWorkspace();
        var path = CreateArchive(workspace, descriptor, resource, entry);
        var result = await CliWorkspace.Run("data", "preview", path, "--asset", "customers", "--limit", "1", "--columns", "name,id", "--output-format", "json");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var json = JObject.Parse(result.Output);
        Assert.That(json["columns"]!.Values<string>(), Is.EqualTo(new[] { "name", "id" }));
        Assert.That(json["rows"]!.Count(), Is.EqualTo(1));
        Assert.That((string?)json["rows"]![0]![0], Is.EqualTo("Alice"));
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("csv")]
    [TestCase("table")]
    public async Task Preview_PreservesOutputFormats(string format)
    {
        using var workspace = new CliWorkspace();
        var path = CreateArchive(workspace);
        var result = await CliWorkspace.Run("data", "preview", path, "--asset", "customers", "--output-format", format);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Output, Does.Contain("Alice"));
        if (format == "csv") Assert.That(result.Output.Replace("\r\n", "\n"), Does.StartWith("id,name\n1,Alice\n"));
    }

    [TestCase(false, "exactly one supported contract descriptor")]
    [TestCase(true, "data/customers.csv")]
    public async Task Preview_ReportsMissingDescriptorOrResourceAndReleasesArchive(bool descriptor, string message)
    {
        using var workspace = new CliWorkspace();
        var path = CreateArchive(workspace, descriptor ? "datapackage.json" : "other.json", entry: "other.csv");
        var result = await CliWorkspace.Run("data", "preview", path, "--asset", "customers");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain(message));
        Assert.That(result.Output, Is.Empty);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("cancellation")]
    public async Task Preview_ReleasesEntryAndArchive(string outcome)
    {
        using var workspace = new CliWorkspace();
        var path = CreateArchive(workspace);
        var source = await ContractSource.LoadAsync(path, "auto", CancellationToken.None, retainArchive: true);
        var archive = source.Archive!;
        try
        {
            var contract = source.Map().RequireValue();
            var asset = contract.Assets.Single(asset => asset.Name == "customers");
            using var cancellation = new CancellationTokenSource();
            var streams = new TrackingResolver(new PreviewService.ArchiveStreamResolver(archive, source.ArchiveDescriptor!), outcome == "cancellation" ? cancellation : null);
            var factory = PreviewService.CreateFactory(source.BaseUri, streams: streams);
            if (outcome == "cancellation")
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await PreviewService.ReadAsync(factory, PreviewService.Request(contract, asset), 1, null, cancellation.Token));
            else if (outcome == "failure")
                await Assert.ThrowsAsync<ContractOperationException>(async () => await PreviewService.ReadAsync(factory, PreviewService.Request(contract, asset), 1, "missing", cancellation.Token));
            else
                await PreviewService.ReadAsync(factory, PreviewService.Request(contract, asset), 1, null, cancellation.Token);
            Assert.That(streams.Opened, Is.Not.Null);
            Assert.That(streams.Opened!.CanRead, Is.False, "The reader must dispose the archive entry stream.");
        }
        finally { source.Dispose(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await archive.OpenAsync("data/customers.csv"));
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task Preview_HttpArchiveWithQueryReadsArchiveEntries()
    {
        using var workspace = new CliWorkspace();
        var body = File.ReadAllBytes(CreateArchive(workspace));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
        });
        var result = await CliWorkspace.Run("data", "preview", $"http://127.0.0.1:{port}/dataset.{suffix.ToUpperInvariant()}?download=1#ignored", "--asset", "customers", "--output-format", "json");
        await serve;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That((string?)JObject.Parse(result.Output)["rows"]![0]![1], Is.EqualTo("Alice"));
    }

    private sealed class TrackingResolver(IEndpointStreamResolver inner, CancellationTokenSource? cancellation) : IEndpointStreamResolver
    {
        public Stream? Opened { get; private set; }
        public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            Opened = await inner.OpenAsync(path, cancellationToken);
            cancellation?.Cancel();
            return Opened;
        }
    }

    private string CreateArchive(CliWorkspace workspace, string descriptor = "datapackage.json", string resource = "data/customers.csv", string entry = "data/customers.csv")
    {
        var path = System.IO.Path.Combine(workspace.Path, $"dataset.{suffix}");
        var document = new JObject { ["name"] = "sample", ["resources"] = new JArray(
            new JObject { ["name"] = "orders", ["path"] = "not-opened.csv", ["format"] = "csv" },
            new JObject { ["name"] = "customers", ["path"] = resource, ["format"] = "csv", ["dialect"] = new JObject { ["header"] = true } }) }.ToString();
        const string csv = "id,name\r\n1,Alice\r\n2,Bob\r\n";
        if (suffix == "zip")
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(archive.CreateEntry(descriptor).Open())) writer.Write(document);
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write(csv);
        }
        else
        {
            using var file = File.Create(path);
            using Stream output = suffix switch
            {
                "tar.gz" => new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true),
                "tar.br" => new BrotliStream(file, CompressionLevel.SmallestSize, leaveOpen: true),
                _ => file
            };
            using var archive = new TarWriter(output, leaveOpen: true);
            // Resources can precede the descriptor in a sequential TAR archive.
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            archive.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, entry) { DataStream = data });
            using var metadata = new MemoryStream(Encoding.UTF8.GetBytes(document));
            archive.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, descriptor) { DataStream = metadata });
        }
        return path;
    }
}