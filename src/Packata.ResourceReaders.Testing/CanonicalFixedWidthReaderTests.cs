using System.Text;
using System.IO.Compression;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.FixedWidth;

namespace Packata.ResourceReaders.Testing;

public class CanonicalFixedWidthReaderTests
{
    [TestCase("fixed-width", null)]
    [TestCase("fixedwidth", null)]
    [TestCase("fwf", null)]
    [TestCase(null, "text/x-fixed-width")]
    public async Task OpenAsync_reads_fixed_width_aliases_and_media_type(string? name, string? mediaType)
    {
        using var reader = await Factory("01alpha\n").OpenAsync(Endpoint(new DataFormat(name, mediaType,
            Options: Options([2, 5]))), Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader["id"], Is.EqualTo(1));
            Assert.That(reader["name"], Is.EqualTo("alpha"));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
        });
    }

    [Test]
    public async Task OpenAsync_supports_explicit_offsets()
    {
        var options = Options([2, 5]);
        options["offsets"] = new[] { 0, 3 };
        using var reader = await Factory("01 alpha\n").OpenAsync(Endpoint(new DataFormat("fixed-width", Options: options)), Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_supports_configured_extension_mapping()
    {
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.dat"]));
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.Formats.AddExtension(".dat", DataFormatNames.FixedWidth);
            options.AddFixedWidth(fixedWidth =>
                fixedWidth.AddLayoutResolver(new ConstantLayoutResolver()));
        }, new Resolver("01alpha\n"));

        using var reader = await factory.OpenAsync(endpoint, Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_supports_configured_format_alias()
    {
        var endpoint = Endpoint(new DataFormat("legacy-fixed", Options: Options([2, 5])));
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.Formats.AddAlias("legacy-fixed", DataFormatNames.FixedWidth);
            options.AddFixedWidth();
        }, new Resolver("01alpha\n"));

        using var reader = await factory.OpenAsync(endpoint, Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_prefers_explicit_format_over_extension_mapping()
    {
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.dat"]), new DataFormat("fixed-width", Options: Options([2, 5])));
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.Formats.AddExtension(".dat", "csv");
            options.AddFixedWidth();
        }, new Resolver("01alpha\n"));

        using var reader = await factory.OpenAsync(endpoint, Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_reports_when_no_layout_resolver_can_resolve()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Factory("01alpha\n").OpenAsync(
                Endpoint(new DataFormat("fixed-width")), Schema()));

        Assert.That(exception!.Message, Does.Contain("no registered layout resolver"));
    }

    [TestCaseSource(nameof(InvalidLayouts))]
    public void OpenAsync_rejects_invalid_layouts(Dictionary<string, object?> options)
    {
        Assert.That(async () => await Factory("01alpha\n")
            .OpenAsync(Endpoint(new DataFormat("fixed-width", Options: options)), Schema()),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Read_rejects_short_and_long_records_by_default()
    {
        using var shortReader = await Factory("01a\n").OpenAsync(
            Endpoint(new DataFormat("fixed-width", Options: Options([2, 5]))), Schema());
        using var longReader = await Factory("01alpha-extra\n").OpenAsync(
            Endpoint(new DataFormat("fixed-width", Options: Options([2, 5]))), Schema());

        Assert.Multiple(() =>
        {
            Assert.That(() => shortReader.Read(), Throws.Exception);
            Assert.That(() => longReader.Read(), Throws.Exception);
        });
    }

    [Test]
    public async Task OpenAsync_allows_trailing_characters_when_configured()
    {
        var options = Options([2, 5]);
        options["allowTrailingCharacters"] = true;
        using var reader = await Factory("01alpha-extra\n").OpenAsync(
            Endpoint(new DataFormat("fixed-width", Options: options)), Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_reads_compressed_multiple_paths()
    {
        var resolver = new DictionaryResolver(
            ("one.fwf.gz", Compress("01alpha\n")),
            ("two.fwf.gz", Compress("02bravo\n")));
        using var reader = await Factory(resolver).OpenAsync(
            new DataEndpoint("data", "data", EndpointKind.File, null,
                new PathLocation(["one.fwf.gz", "two.fwf.gz"]),
                new DataFormat("fixed-width", Compression: "gzip", Options: Options([2, 5]))), Schema());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(2));
    }

    [Test]
    public async Task Disposing_reader_disposes_the_source_stream()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes("01alpha\n"));
        var reader = await Factory(new SingleStreamResolver(stream)).OpenAsync(
            Endpoint(new DataFormat("fixed-width", Options: Options([2, 5]))), Schema());

        reader.Dispose();

        Assert.That(stream.Disposed, Is.True);
    }

    private static IEnumerable<TestCaseData> InvalidLayouts()
    {
        yield return new TestCaseData(new Dictionary<string, object?>()).SetName("missing widths");
        yield return new TestCaseData(Options([2])).SetName("missing field");
        yield return new TestCaseData(Options([2, -1])).SetName("negative width");
        var overlap = Options([3, 4]); overlap["offsets"] = new[] { 0, 2 };
        yield return new TestCaseData(overlap).SetName("overlap");
        var outOfRange = Options([2, 5]); outOfRange["recordWidth"] = 6;
        yield return new TestCaseData(outOfRange).SetName("outside record width");
    }

    private static Dictionary<string, object?> Options(int[] widths) => new() { ["widths"] = widths };
    private static DataSchema Schema() => new([new DataField("id", "integer"), new DataField("name", "string")]);
    private static DataEndpoint Endpoint(DataFormat format) =>
        new("data", "data", EndpointKind.File, null, new PathLocation(["data.bin"]), format);
    private static ResourceReaderFactory Factory(string content) => ResourceReaderFactory.Create(
        options => options.AddFixedWidth(), new Resolver(content));
    private static ResourceReaderFactory Factory(IEndpointStreamResolver resolver) =>
        ResourceReaderFactory.Create(options => options.AddFixedWidth(), resolver);

    private static byte[] Compress(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(value));
        return output.ToArray();
    }

    private sealed class Resolver(string content) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
    }

    private sealed class DictionaryResolver(params (string Path, byte[] Content)[] values) : IEndpointStreamResolver
    {
        private readonly Dictionary<string, byte[]> _values = values.ToDictionary(value => value.Path, value => value.Content);
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(_values[path], writable: false));
    }

    private sealed class SingleStreamResolver(Stream stream) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(stream);
    }

    private sealed class ConstantLayoutResolver : IFixedWidthLayoutResolver
    {
        public ValueTask<FixedWidthLayout?> ResolveAsync(FixedWidthLayoutContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<FixedWidthLayout?>(new FixedWidthLayout([0, 2], [2, 5]));
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
