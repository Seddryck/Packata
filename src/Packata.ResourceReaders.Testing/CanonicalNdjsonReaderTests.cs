using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Ndjson;

namespace Packata.ResourceReaders.Testing;

public class CanonicalNdjsonReaderTests
{
    [TestCase("ndjson", null)]
    [TestCase("jsonl", null)]
    [TestCase(null, "application/x-ndjson")]
    public async Task OpenAsync_reads_ndjson_aliases_and_media_types(string? name, string? mediaType)
    {
        var factory = Factory(new MemoryResolver(("data.bin", Bytes("{\"id\":1,\"name\":\"alpha\"}\n"))));
        using var reader = await factory.OpenAsync(Endpoint(["data.bin"], new DataFormat(name, mediaType)));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public async Task OpenAsync_applies_schema_order_types_and_missing_values()
    {
        const string content = "{\"extra\":true,\"name\":\"alpha\",\"id\":1}\n{\"id\":2,\"name\":null}\n{\"id\":3}\n";
        var schema = new DataSchema([new DataField("id", "integer"), new DataField("name", "string")]);
        using var reader = await Factory(new MemoryResolver(("data.ndjson", Bytes(content))))
            .OpenAsync(Endpoint(["data.ndjson"]), schema);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("id"));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
            Assert.That(reader["id"], Is.EqualTo(1));
            Assert.That(reader["name"], Is.EqualTo("alpha"));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(1), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(1), Is.True);
    }

    [Test]
    public async Task OpenAsync_reads_gzip_and_multiple_paths()
    {
        var resolver = new MemoryResolver(
            ("one.ndjson.gz", Compress("{\"id\":1}\n")),
            ("two.ndjson.gz", Compress("{\"id\":2}\n")));
        var schema = new DataSchema([new DataField("id", "integer")]);
        using var reader = await Factory(resolver).OpenAsync(Endpoint(["one.ndjson.gz", "two.ndjson.gz"]), schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(2));
    }

    [Test]
    public async Task OpenAsync_rejects_malformed_ndjson()
    {
        using var reader = await Factory(new MemoryResolver(("data.ndjson", Bytes("{\"id\":1\n"))))
            .OpenAsync(Endpoint(["data.ndjson"]), new DataSchema([new DataField("id", "integer")]));

        Assert.That(() => reader.Read(), Throws.Exception);
    }

    [Test]
    public async Task Disposing_reader_disposes_the_source_stream()
    {
        var stream = new TrackingStream(Bytes("{\"id\":1}\n"));
        var reader = await Factory(new SingleStreamResolver(stream)).OpenAsync(Endpoint(["data.ndjson"]));

        reader.Dispose();

        Assert.That(stream.Disposed, Is.True);
    }

    private static ResourceReaderFactory Factory(IEndpointStreamResolver resolver) =>
        ResourceReaderFactory.Create(options => options.AddNdjson(), resolver);

    private static DataEndpoint Endpoint(IReadOnlyList<string> paths, DataFormat? format = null) =>
        new("data", "data", EndpointKind.File, null, new PathLocation(paths), format);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Compress(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Bytes(value));
        return output.ToArray();
    }

    private sealed class MemoryResolver(params (string Path, byte[] Content)[] values) : IEndpointStreamResolver
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

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
