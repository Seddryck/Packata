using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class CanonicalDelimitedReaderTests
{
    [TestCase("data.csv", null, ',', "alpha")]
    [TestCase("data.tsv", null, '\t', "alpha")]
    [TestCase("data.psv", null, '|', "alpha")]
    [TestCase("data.unknown", "text/csv", ',', "alpha")]
    [TestCase("data.unknown", "text/tsv;charset=utf-8", '\t', "alpha")]
    [TestCase("data.unknown", "text/tab-separated-values", '\t', "alpha")]
    [TestCase("data.unknown", "text/psv", '|', "alpha")]
    public async Task OpenAsync_infers_delimited_dialect(string path, string? mediaType, char delimiter,
        string expected)
    {
        var content = $"id{delimiter}name\r\n1{delimiter}{expected}\r\n";
        var resolver = new DictionaryResolver((path, Encoding.UTF8.GetBytes(content)));
        var endpoint = Endpoint([path], new DataFormat(null, mediaType));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(endpoint);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader["id"], Is.EqualTo("1"));
            Assert.That(reader["name"], Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task OpenAsync_prefers_explicit_dialect_options()
    {
        var resolver = new DictionaryResolver(("data.csv", Encoding.UTF8.GetBytes("id;name\n1;alpha\n")));
        var endpoint = Endpoint(["data.csv"], new DataFormat("csv", Options: new Dictionary<string, object?>
        {
            ["delimiter"] = ';', ["lineTerminator"] = "\n", ["header"] = true
        }));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(endpoint);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_reads_multiple_delimited_paths()
    {
        var resolver = new DictionaryResolver(
            ("one.csv", Encoding.UTF8.GetBytes("id,name\r\n1,alpha\r\n")),
            ("two.csv", Encoding.UTF8.GetBytes("id,name\r\n2,beta\r\n")));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(Endpoint(["one.csv", "two.csv"]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("beta"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void OpenAsync_rejects_incoherent_path_formats()
    {
        var factory = new ResourceReaderFactory(new DictionaryResolver());

        Assert.That(async () => await factory.OpenAsync(Endpoint(["one.csv", "two.tsv"])),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task OpenAsync_infers_gzip_compression_from_path()
    {
        var bytes = Compress("id,name\r\n1,alpha\r\n");
        var resolver = new DictionaryResolver(("data.csv.gz", bytes));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(Endpoint(["data.csv.gz"]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_applies_canonical_schema_types()
    {
        var resolver = new DictionaryResolver(("data.csv", Encoding.UTF8.GetBytes("id,name\r\n1,alpha\r\n")));
        var schema = new DataSchema([new DataField("id", "integer"), new DataField("name", "string")]);

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(Endpoint(["data.csv"]), schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(1));
    }

    [Test]
    public void OpenAsync_rejects_an_endpoint_without_paths()
    {
        var factory = new ResourceReaderFactory(new DictionaryResolver());
        var endpoint = Endpoint([]);

        Assert.That(async () => await factory.OpenAsync(endpoint), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void OpenAsync_disposes_opened_streams_when_a_later_path_fails()
    {
        var opened = new TrackingStream(Encoding.UTF8.GetBytes("id,name\r\n1,alpha\r\n"));
        var resolver = new SequencedResolver(opened, new IOException("unavailable"));
        var factory = new ResourceReaderFactory(resolver);

        Assert.That(async () => await factory.OpenAsync(Endpoint(["one.csv", "two.csv"])),
            Throws.TypeOf<IOException>());
        Assert.That(opened.Disposed, Is.True);
    }

    [Test]
    public async Task Disposing_reader_disposes_its_stream()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes("id,name\r\n1,alpha\r\n"));
        var reader = await new ResourceReaderFactory(new SequencedResolver(stream))
            .OpenAsync(Endpoint(["data.csv"]));

        reader.Dispose();

        Assert.That(stream.Disposed, Is.True);
    }

    private static DataEndpoint Endpoint(IReadOnlyList<string> paths, DataFormat? format = null) =>
        new("data", "data", EndpointKind.File, null, new PathLocation(paths), format);

    private static byte[] Compress(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(value));
        return output.ToArray();
    }

    private sealed class DictionaryResolver(params (string Path, byte[] Content)[] values) : IEndpointStreamResolver
    {
        private readonly Dictionary<string, byte[]> _values = values.ToDictionary(x => x.Path, x => x.Content);
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(_values[path], writable: false));
    }

    private sealed class SequencedResolver(params object[] results) : IEndpointStreamResolver
    {
        private int _index;
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            var result = results[_index++];
            return result is Exception error ? ValueTask.FromException<Stream>(error) : ValueTask.FromResult((Stream)result);
        }
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
