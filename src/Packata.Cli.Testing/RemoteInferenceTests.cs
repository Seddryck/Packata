using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using Packata.Testing;

namespace Packata.Cli.Testing;

[TestFixture("json")]
[TestFixture("yaml")]
[TestFixture("odcs")]
public sealed class RemoteInferenceTests(string standard)
{
    private string Descriptor(string path = "data.csv", string format = "csv") => (standard switch
    {
        "odcs" => CliWorkspace.Odcs,
        "yaml" => CliWorkspace.PackageYaml,
        _ => CliWorkspace.PackageJson
    }).Replace("customers.csv", path).Replace("csv", format);
    private string Name => standard == "json" ? "datapackage.json" : "contract.yaml";
    private static byte[] Data(string format) => format switch
    {
        "parquet" => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "iris.parquet")),
        "xlsx" => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "my-book.xlsx")),
        _ => Encoding.UTF8.GetBytes("id,name\n1,Alice\n2,Bob\n")
    };

    [TestCase("csv", false)]
    [TestCase("csv", true)]
    [TestCase("xlsx", false)]
    [TestCase("xlsx", true)]
    [TestCase("parquet", false)]
    [TestCase("parquet", true)]
    public async Task Infer_RemoteDataFromLocalOrRemoteDescriptor(string format, bool remoteDescriptor)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        var path = "download?sig=private-signature&X-Amz-Credential=private-credential";
        var descriptor = Descriptor(remoteDescriptor ? path : server.Url + path, format);
        server.Routes["/contracts/" + Name] = (200, Encoding.UTF8.GetBytes(descriptor));
        server.Routes["/contracts/" + path] = (200, Data(format));
        server.Routes["/" + path] = (200, Data(format));
        var source = remoteDescriptor ? server.Url + "contracts/" + Name : workspace.Write(Name, descriptor);
        var result = await CliWorkspace.Run("infer", source, "--asset", "customers");
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("Inferred asset").And.Not.Contain("private-signature").And.Not.Contain("private-credential").And.Not.Contain("packata-stage-"));
        if (!remoteDescriptor) Assert.That(File.ReadAllText(source), Is.EqualTo(descriptor));
    }

    [TestCase("zip", false)]
    [TestCase("tar", false)]
    [TestCase("tar.gz", false)]
    [TestCase("tar.br", false)]
    [TestCase("zip", true)]
    [TestCase("tar", true)]
    [TestCase("tar.gz", true)]
    [TestCase("tar.br", true)]
    public async Task Infer_ArchiveRelativeFilesAndRemoteFiles(string suffix, bool remote)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        foreach (var format in new[] { "csv", "xlsx", "parquet" })
        {
            var bytes = Archive(suffix, ("nested/" + Name, Encoding.UTF8.GetBytes(Descriptor("../data/rows." + format, format))),
                ("data/rows." + format, Data(format)));
            var source = Path.Combine(workspace.Path, "dataset." + suffix);
            File.WriteAllBytes(source, bytes);
            server.Routes["/dataset." + suffix + "?sig=private"] = (200, bytes);
            if (remote) source = server.Url + "dataset." + suffix + "?sig=private";
            var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers");
            Assert.That(result.ExitCode, Is.Zero, format + result.Output + result.Error);
            if (!remote) { using var exclusive = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        }

        // Absolute HTTP resources remain independent of the archive member directory.
        server.Routes["/data"] = (200, Data("csv"));
        var absolute = Archive(suffix, ("nested/" + Name, Encoding.UTF8.GetBytes(Descriptor(server.Url + "data"))));
        var local = Path.Combine(workspace.Path, "absolute." + suffix);
        File.WriteAllBytes(local, absolute);
        var output = Path.Combine(workspace.Path, "enriched");
        var persisted = await CliWorkspace.Run("schema", "infer", local, "--asset", "customers", "--output", output);
        Assert.That(persisted.ExitCode, Is.Zero, persisted.Output + persisted.Error);
        using var reloaded = ContractDocument.Load(output);
        Assert.That(((Packata.Core.Contracts.PathLocation)reloaded.Contract.Endpoints.Single().Location).Paths.Single(), Is.EqualTo(server.Url + "data"));
    }

    [Test]
    public async Task Infer_RemoteOutputNormalizesAllPathsAndCanBeReloaded()
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        server.Routes["/contracts/" + Name] = (200, Encoding.UTF8.GetBytes(Descriptor("../data.csv")));
        server.Routes["/data.csv"] = (200, Data("csv"));
        var target = Path.Combine(workspace.Path, "output");
        var result = await CliWorkspace.Run("schema", "infer", server.Url + "contracts/" + Name, "--asset", "customers", "--output", target);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("CLI131"));
        using var document = ContractDocument.Load(target);
        Assert.That(((Packata.Core.Contracts.PathLocation)document.Contract.Endpoints.Single().Location).Paths.Single(), Is.EqualTo(server.Url + "data.csv"));
        var again = await CliWorkspace.Run("schema", "infer", target, "--asset", "customers");
        Assert.That(again.ExitCode, Is.Zero, again.Output + again.Error);
        var before = File.ReadAllText(target);
        var refused = await CliWorkspace.Run("schema", "infer", server.Url + "contracts/" + Name, "--asset", "customers", "--output", target);
        Assert.That(refused.ExitCode, Is.Not.Zero);
        Assert.That(File.ReadAllText(target), Is.EqualTo(before));
    }

    [TestCase("zip")]
    [TestCase("tar")]
    [TestCase("tar.gz")]
    [TestCase("tar.br")]
    public async Task Infer_RefusesDetachedOutputAndArchiveWrites(string suffix)
    {
        using var workspace = new CliWorkspace();
        var source = Path.Combine(workspace.Path, "dataset." + suffix);
        var bytes = Archive(suffix, (Name, Encoding.UTF8.GetBytes(Descriptor())), ("data.csv", Data("csv")));
        File.WriteAllBytes(source, bytes);
        var output = Path.Combine(workspace.Path, "output");
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--output", output);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("archive-relative"));
        Assert.That(File.Exists(output), Is.False);
        var write = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--write");
        Assert.That(write.ExitCode, Is.Not.Zero);
        Assert.That(write.Output, Does.Contain("ordinary local descriptor"));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(bytes));
    }

    [TestCase("missing")]
    [TestCase("ambiguous")]
    [TestCase("resource")]
    [TestCase("traversal")]
    [TestCase("truncated")]
    public async Task Infer_ArchiveFailuresLeaveNoOutput(string failure)
    {
        using var workspace = new CliWorkspace();
        var source = Path.Combine(workspace.Path, "dataset.zip");
        var entries = new List<(string, byte[])> { (failure == "missing" ? "other.txt" : Name,
            Encoding.UTF8.GetBytes(Descriptor(failure == "traversal" ? "../data.csv" : "data.csv"))) };
        if (failure == "ambiguous") entries.Add(("other/" + Name, Encoding.UTF8.GetBytes(Descriptor())));
        var bytes = Archive("zip", entries.ToArray());
        File.WriteAllBytes(source, failure == "truncated" ? bytes[..20] : bytes);
        // Display-only reaches resource resolution; output is independently refused before reading resources.
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers");
        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        using var exclusive = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task Infer_HttpErrorsAreSecretSafeAndLeaveNoOutput()
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        server.Routes["/missing?sig=private-signature"] = (404, []);
        var target = Path.Combine(workspace.Path, "output");
        var missing = await CliWorkspace.Run("schema", "infer", server.Url + "missing?sig=private-signature", "--asset", "customers", "--output", target);
        Assert.That(missing.ExitCode, Is.Not.Zero);
        Assert.That(missing.Output + missing.Error, Does.Not.Contain("private-signature"));
        Assert.That(File.Exists(target), Is.False);
        var source = workspace.Write(Name, Descriptor(server.Url + "missing?sig=private-signature"));
        var data = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--output", target);
        Assert.That(data.ExitCode, Is.Not.Zero);
        Assert.That(data.Output + data.Error, Does.Not.Contain("private-signature"));
        Assert.That(File.Exists(target), Is.False);
        var write = await CliWorkspace.Run("schema", "infer", server.Url + "not-requested", "--asset", "customers", "--write");
        Assert.That(write.Output, Does.Contain("ordinary local descriptor"));
    }

    [Test]
    public async Task Infer_FollowsExistingHttpRedirectPolicy()
    {
        await using var server = new HttpFixture();
        server.Routes["/old/redirect"] = (302, Encoding.ASCII.GetBytes(server.Url + "contracts/" + Name));
        server.Routes["/contracts/" + Name] = (200, Encoding.UTF8.GetBytes(Descriptor("redirect-data")));
        server.Routes["/contracts/redirect-data"] = (302, Encoding.ASCII.GetBytes(server.Url + "data.csv"));
        server.Routes["/data.csv"] = (200, Data("csv"));
        var result = await CliWorkspace.Run("schema", "infer", server.Url + "old/redirect", "--asset", "customers");
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
    }

    [TestCase("s3://bucket/data.csv")]
    [TestCase("ftp://example.org/data.csv")]
    public async Task Infer_UnsupportedResourceSchemesLeaveNoOutput(string path)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write(Name, Descriptor(path));
        var target = Path.Combine(workspace.Path, "output");
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--output", target);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(File.Exists(target), Is.False);
    }

    [TestCase("csv")]
    [TestCase("xlsx")]
    [TestCase("parquet")]
    public async Task Infer_CancelsPendingHttpDataAndCleansStaging(string format)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        server.StallPath = "/pending";
        var source = workspace.Write(Name, Descriptor(server.Url + "pending", format));
        using var document = await ContractDocument.LoadAsync(source, "auto", CancellationToken.None);
        var before = Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp");
        using var cancellation = new CancellationTokenSource();
        var asset = document.Contract.Assets.Single();
        var operation = Task.Run(async () => await InferenceService.InferFileAsync(document, asset,
            document.Contract.Endpoints.Single(), new InferSettings { Source = source, Asset = asset.Id }, [], cancellation.Token));
        await server.HeadersSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThatAsync(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp"), Is.EquivalentTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Infer_CancelsPendingDescriptorOrArchiveDownload(bool archive)
    {
        await using var server = new HttpFixture();
        var path = archive ? "pending.zip" : "pending";
        server.StallPath = "/" + path;
        using var cancellation = new CancellationTokenSource();
        var before = Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp");
        var operation = ContractDocument.LoadAsync(server.Url + path, "auto", cancellation.Token);
        await server.HeadersSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThatAsync(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp"), Is.EquivalentTo(before));
    }

    [Test]
    public async Task Infer_ExcelTransferLimitLeavesNoOutput()
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        server.Routes["/data.xlsx"] = (200, Data("xlsx"));
        var source = workspace.Write(Name, Descriptor(server.Url + "data.xlsx", "xlsx"));
        var target = Path.Combine(workspace.Path, "output");
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--maximum-bytes", "4", "--output", target);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("Workbook exceeds MaximumBytes"));
        Assert.That(File.Exists(target), Is.False);
    }

    internal static byte[] Archive(string suffix, params (string Name, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        if (suffix == "zip")
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var entry in entries) { using var stream = zip.CreateEntry(entry.Name).Open(); stream.Write(entry.Bytes); }
        }
        else
        {
            using Stream output = suffix switch
            {
                "tar.gz" => new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                "tar.br" => new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                _ => buffer
            };
            using var tar = new TarWriter(output, leaveOpen: true);
            foreach (var entry in entries)
            {
                using var data = new MemoryStream(entry.Bytes);
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, entry.Name) { DataStream = data });
            }
        }
        return buffer.ToArray();
    }

    // Close-delimited bodies deliberately omit Content-Length, exercising bounded streaming/staging.
    internal sealed class HttpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(90));
        private readonly Task _serve;
        internal Dictionary<string, (int Status, byte[] Body)> Routes { get; } = new();
        internal Dictionary<string, long> DeclaredLengths { get; } = new();
        internal string? StallPath { get; set; }
        internal TaskCompletionSource HeadersSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Url { get; }
        internal HttpFixture()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serve = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    var path = request!.Split(' ')[1];
                    var route = Routes.GetValueOrDefault(path, (200, Array.Empty<byte>()));
                    var location = route.Item1 == 302 ? "Location: " + Encoding.ASCII.GetString(route.Item2) + "\r\n" : "";
                    var length = DeclaredLengths.TryGetValue(path, out var size) ? $"Content-Length: {size}\r\n" : "";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {route.Item1} Test\r\n{location}{length}Connection: close\r\n\r\n"), _stop.Token);
                    HeadersSent.TrySetResult();
                    if (path == StallPath) await Task.Delay(Timeout.Infinite, _stop.Token);
                    await stream.WriteAsync(route.Item2, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _serve;
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
