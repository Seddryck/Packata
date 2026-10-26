using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using NUnit.Framework;
using Packata.Core.Storage;

namespace Packata.Storages.Testing;

public class TarDataPackageContainerTests
{
    private string _directory = null!;
    private string _rootDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Directory.CreateTempSubdirectory("packata-tar-tests-").FullName;
        _directory = TarTemporaryDirectory.CreatePrivateDirectory(Path.Combine(_rootDirectory, "private")).FullName;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_rootDirectory, recursive: true);

    private TarArchiveOptions Options => new() { TemporaryDirectory = _directory };

    [Test]
    public async Task Default_spill_directory_is_private_and_files_are_cleaned_up()
    {
        Assert.That(new TarArchiveOptions().TemporaryDirectory, Is.Null);
        var directory = TarTemporaryDirectory.Resolve(null);
        Assert.That(directory, Does.StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        AssertPrivate(directory);
        var before = Directory.GetFiles(directory);
        using (var container = await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"),
            new MemoryStream(Bundle()), new TarArchiveOptions { MemoryThresholdBytes = 0 }))
        {
            using var reader = new StreamReader(await container.OpenAsync("data/one.csv"));
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("id\n1\n"));
            Assert.That(Directory.GetFiles(directory).Except(before).Count(), Is.EqualTo(3));
        }
        Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(before));
    }

    [Test]
    public async Task Public_spill_directory_is_rejected_before_writing_content()
    {
        if (OperatingSystem.IsWindows())
        {
            var directory = new DirectoryInfo(_directory);
            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        else File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var input = new NonSeekableStream(Bundle());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await TarDataPackageContainer.CreateAsync(
            new Uri("file:///package.tar"), input, Options with { MemoryThresholdBytes = 0 }));
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public async Task Missing_spill_directory_is_rejected_and_input_closed()
    {
        var input = new NonSeekableStream(Bundle());
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await TarDataPackageContainer.CreateAsync(
            new Uri("file:///package.tar"), input,
            Options with { MemoryThresholdBytes = 0, TemporaryDirectory = Path.Combine(_directory, "missing") }));
        Assert.That(input.Disposed, Is.True);
    }

    private static void AssertPrivate(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectoryInfo(path).GetAccessControl();
            Assert.That(security.AreAccessRulesProtected, Is.True);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow)
                    Assert.That(rule.IdentityReference, Is.EqualTo(identity.User));
            }
        }
        else Assert.That(File.GetUnixFileMode(path), Is.EqualTo(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    [TestCase(ArchiveCompression.None, "tar")]
    [TestCase(ArchiveCompression.Gzip, "tar.gz")]
    [TestCase(ArchiveCompression.Brotli, "tar.br")]
    public async Task Locator_reads_descriptor_resources_and_independent_streams(ArchiveCompression compression, string suffix)
    {
        var path = Path.Combine(_directory, $"package.{suffix}");
        await File.WriteAllBytesAsync(path, Bundle(compression));
        var locator = Locator(suffix, compression);
        var handle = await locator.LocateAsync(new Uri(path));
        using var container = handle.Container;
        await handle.ValidateAsync();
        using var first = await container.OpenAsync("data/one.csv");
        using var second = await container.OpenAsync("data\\one.csv");
        Assert.That(first.ReadByte(), Is.EqualTo((int)'i'));
        Assert.That(second.ReadByte(), Is.EqualTo((int)'i'));
        Assert.That(first.ReadByte(), Is.EqualTo((int)'d'));
        using var descriptor = new StreamReader(await container.OpenAsync("datapackage.json"));
        Assert.That(await descriptor.ReadToEndAsync(), Does.Contain("resources"));
        Assert.That(await ((IDocumentContainerListable)container).ListAsync(),
            Is.EquivalentTo(new[] { "data/one.csv", "two.csv", "datapackage.json" }));
        Assert.That(await container.ExistsAsync("missing.csv"), Is.False);
        await Assert.ThrowsAsync<FileNotFoundException>(async () => { using var missing = await container.OpenAsync("missing.csv"); });
    }

    [TestCase(ArchiveCompression.None, "tar")]
    [TestCase(ArchiveCompression.Gzip, "tar.gz")]
    [TestCase(ArchiveCompression.Brotli, "tar.br")]
    public async Task Http_locator_preserves_query_and_uses_nonseekable_input(ArchiveCompression compression, string suffix)
    {
        var input = new NonSeekableStream(Bundle(compression));
        using var handler = new ResponseHandler(input);
        using var client = new HttpClient(handler);
        var locator = new DataPackageLocatorBuilder()
            .Register("https", b => b.UseHttp(client))
            .Register("gz", b => b.UseZipArchive()) // Longest suffix must win.
            .Register(suffix, b => b.UseTarArchive(compression, Options)).Build();
        var uri = new Uri($"https://example.test/packages/package.{suffix.ToUpperInvariant()}?key=value#fragment");
        Assert.That(locator.CanHandle(uri), Is.True);
        var handle = await locator.LocateAsync(uri);
        using var container = handle.Container;
        await handle.ValidateAsync();
        Assert.That(handler.RequestUri!.Query, Is.EqualTo("?key=value"));
        Assert.That(input.Disposed, Is.True);
    }

    [TestCase("tar", ArchiveCompression.None)]
    [TestCase("tar.gz", ArchiveCompression.Gzip)]
    [TestCase("tar.br", ArchiveCompression.Brotli)]
    public async Task Unregistered_standalone_compression_is_rejected(string suffix, ArchiveCompression compression)
    {
        var locator = Locator(suffix, compression);
        Assert.That(locator.CanHandle(new Uri("file:///C:/data.gz")), Is.False);
        Assert.That(locator.CanHandle(new Uri("file:///C:/data.br")), Is.False);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await locator.LocateAsync(new Uri("file:///C:/data.gz")));
    }

    [Test]
    public async Task Spilled_files_are_independent_and_deleted_on_disposal()
    {
        var input = new NonSeekableStream(Bundle());
        var container = await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"), input,
            Options with { MemoryThresholdBytes = 0 });
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(3));
        using (var one = await container.OpenAsync("data/one.csv"))
        using (var two = await container.OpenAsync("data/one.csv"))
        {
            Assert.That(one.ReadByte(), Is.EqualTo(two.ReadByte()));
        }
        container.Dispose();
        container.Dispose();
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await container.OpenAsync("two.csv"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await container.ExistsAsync("two.csv"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await container.ListAsync());
    }

    [TestCase("entries")]
    [TestCase("entry-size")]
    [TestCase("total-size")]
    [TestCase("archive-size")]
    public async Task Limits_reject_archive_and_clean_up_prior_entries(string limit)
    {
        var options = Options with { MemoryThresholdBytes = 0 };
        options = limit switch
        {
            "entries" => options with { MaxEntries = 1 },
            "entry-size" => options with { MaxEntryBytes = 20 },
            "total-size" => options with { MaxTotalBytes = 20 },
            _ => options with { MaxArchiveBytes = 1100 }
        };
        var input = new NonSeekableStream(Bundle());
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"), input, options));
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("../escape.csv")]
    [TestCase("/absolute.csv")]
    [TestCase("C:/absolute.csv")]
    [TestCase("data/../../escape.csv")]
    public async Task Unsafe_paths_are_rejected(string name)
    {
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"),
                new MemoryStream(Tar((name, "bad"))), Options));
    }

    [Test]
    public async Task Duplicate_normalized_paths_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"),
                new MemoryStream(Tar(("a.csv", "a"), ("./a.csv", "b"))), Options));
    }

    [Test]
    public async Task Directories_are_skipped_and_links_rejected()
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, leaveOpen: true))
        {
            writer.WriteEntry(new UstarTarEntry(TarEntryType.Directory, "data/"));
        }
        using (var container = await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"),
            new MemoryStream(output.ToArray()), Options))
            Assert.That(await container.ListAsync(), Is.Empty);

        using var links = new MemoryStream();
        using (var writer = new TarWriter(links, leaveOpen: true))
            writer.WriteEntry(new UstarTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "target" });
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"),
                new MemoryStream(links.ToArray()), Options));
    }

    [TestCase(0)]
    [TestCase(10)]
    [TestCase(520)]
    public void Truncated_tar_is_rejected(int length)
    {
        var input = new NonSeekableStream(Bundle()[..length]);
        Assert.That(async () => await TarDataPackageContainer.CreateAsync(new Uri("file:///package.tar"), input, Options),
            Throws.InstanceOf<IOException>());
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase(ArchiveCompression.Gzip, "tar.gz")]
    [TestCase(ArchiveCompression.Brotli, "tar.br")]
    public async Task Invalid_compression_is_rejected_and_source_closed(ArchiveCompression compression, string suffix)
    {
        var input = new NonSeekableStream(Encoding.UTF8.GetBytes("this is not a compressed TAR archive"));
        using var handler = new ResponseHandler(input);
        using var client = new HttpClient(handler);
        var locator = new DataPackageLocatorBuilder().Register("https", b => b.UseHttp(client))
            .Register(suffix, b => b.UseTarArchive(compression, Options)).Build();
        Assert.That(async () => await locator.LocateAsync(new Uri($"https://example.test/package.{suffix}")),
            Throws.InstanceOf<InvalidDataException>());
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        await Task.CompletedTask;
    }

    [TestCase(ArchiveCompression.Gzip, "tar.gz")]
    [TestCase(ArchiveCompression.Brotli, "tar.br")]
    public async Task Truncated_compression_is_rejected_and_source_closed(ArchiveCompression compression, string suffix)
    {
        var bytes = Bundle(compression);
        var input = new NonSeekableStream(bytes[..(bytes.Length / 2)]);
        using var handler = new ResponseHandler(input);
        using var client = new HttpClient(handler);
        var locator = new DataPackageLocatorBuilder().Register("https", b => b.UseHttp(client))
            .Register(suffix, b => b.UseTarArchive(compression, Options)).Build();
        await Assert.ThatAsync(async () => await locator.LocateAsync(new Uri($"https://example.test/package.{suffix}")),
            Throws.InstanceOf<IOException>());
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public async Task Failed_materialization_disposes_the_source_container()
    {
        var source = new Moq.Mock<IDocumentContainer>();
        var input = new NonSeekableStream(Bundle());
        source.Setup(container => container.OpenAsync("package.tar")).ReturnsAsync(input);
        var locator = new DataPackageLocator(new Dictionary<string, Func<Uri, IDocumentContainer>>
        {
            ["mock"] = _ => source.Object
        }, new Dictionary<string, Func<Uri, IContainerWrapper>>
        {
            [".tar"] = _ => new TarContainerWrapper(ArchiveCompression.None, Options with { MaxEntries = 1, MemoryThresholdBytes = 0 })
        });
        await Assert.ThrowsAsync<InvalidDataException>(async () => await locator.LocateAsync(new Uri("mock://host/package.tar")));
        source.Verify(container => container.Dispose(), Moq.Times.Once);
        Assert.That(input.Disposed, Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public async Task Successful_materialization_retains_source_ownership_until_disposal()
    {
        var source = new Moq.Mock<IDocumentContainer>();
        source.Setup(container => container.OpenAsync("package.tar")).ReturnsAsync(new NonSeekableStream(Bundle()));
        var locator = new DataPackageLocator(new Dictionary<string, Func<Uri, IDocumentContainer>>
        {
            ["mock"] = _ => source.Object
        }, new Dictionary<string, Func<Uri, IContainerWrapper>>
        {
            [".tar"] = _ => new TarContainerWrapper(ArchiveCompression.None, Options)
        });
        var handle = await locator.LocateAsync(new Uri("mock://host/package.tar"));
        source.Verify(container => container.Dispose(), Moq.Times.Never);
        handle.Container.Dispose();
        handle.Container.Dispose();
        source.Verify(container => container.Dispose(), Moq.Times.Once);
    }

    [Test]
    public async Task Zip_locator_still_opens_descriptor()
    {
        var path = Path.Combine(_directory, "package.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("datapackage.json").Open());
            writer.Write("{}");
        }
        var locator = new DataPackageLocatorBuilder().Register("file", b => b.UseLocalFileSystem())
            .Register("zip", b => b.UseZipArchive()).Build();
        var handle = await locator.LocateAsync(new Uri(path));
        using (handle.Container) await handle.ValidateAsync();
    }

    private IDocumentLocator Locator(string suffix, ArchiveCompression compression) => new DataPackageLocatorBuilder()
        .Register("file", b => b.UseLocalFileSystem())
        .Register(suffix, b => b.UseTarArchive(compression, Options)).Build();

    private static byte[] Bundle(ArchiveCompression compression = ArchiveCompression.None)
    {
        var tar = Tar(("data/one.csv", "id\n1\n"), ("two.csv", "id\n2\n"),
            ("datapackage.json", "{\"resources\":[{\"path\":\"data/one.csv\"},{\"path\":\"two.csv\"}]}"));
        if (compression == ArchiveCompression.None) return tar;
        using var output = new MemoryStream();
        using (Stream encoder = compression == ArchiveCompression.Gzip
            ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(tar);
        return output.ToArray();
    }

    private static byte[] Tar(params (string Name, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
            }
        return output.ToArray();
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class ResponseHandler(Stream stream) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        }
    }
}
