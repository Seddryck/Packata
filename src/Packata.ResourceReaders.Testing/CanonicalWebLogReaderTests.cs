using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.WebLogs;

namespace Packata.ResourceReaders.Testing;

public class CanonicalWebLogReaderTests
{
    private const string CommonLine =
        "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET /index.html HTTP/1.0\" 200 2326\n";

    [TestCase("common-log", null)]
    [TestCase("commonlog", null)]
    [TestCase("clf", null)]
    [TestCase(null, "text/x-common-log")]
    public async Task OpenAsync_reads_common_log_names_and_media_type(string? name, string? mediaType)
    {
        using var reader = await Factory(new Resolver(("data.bin", CommonLine)))
            .OpenAsync(Endpoint(["data.bin"], new DataFormat(name, mediaType)));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader["Request"], Is.EqualTo("GET /index.html HTTP/1.0"));
            Assert.That(reader["StatusCode"], Is.EqualTo(200));
            Assert.That(reader.GetFieldType(reader.GetOrdinal("StatusCode")), Is.EqualTo(typeof(int)));
            Assert.That(reader["ResponseBytes"], Is.EqualTo(2326L));
            Assert.That(reader.IsDBNull(reader.GetOrdinal("Identity")), Is.True);
        });
    }

    [TestCase("w3c-log", null)]
    [TestCase("w3c", null)]
    [TestCase("w3c-extended", null)]
    [TestCase(null, "text/x-w3c-log")]
    public async Task OpenAsync_uses_w3c_field_directives(string? name, string? mediaType)
    {
        const string content = "#Fields: date time c-ip cs-method cs-uri-stem sc-status sc-bytes\n" +
                               "2026-09-30 12:34:56 192.0.2.1 GET /index.html 200 1234\n" +
                               "2026-09-30 12:35:01 192.0.2.2 GET /missing - -\n";
        using var reader = await Factory(new Resolver(("data.bin", content)))
            .OpenAsync(Endpoint(["data.bin"], new DataFormat(name, mediaType)));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(2), Is.EqualTo("c-ip"));
            Assert.That(reader["cs-uri-stem"], Is.EqualTo("/index.html"));
            Assert.That(reader["sc-status"], Is.EqualTo(200));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(reader.GetOrdinal("sc-status")), Is.True);
    }

    [TestCase("common-log", "broken record\n")]
    [TestCase("w3c-log", "2026-09-30 12:34:56\n")]
    public async Task Read_rejects_malformed_web_logs(string format, string content)
    {
        using var reader = await Factory(new Resolver(("data.bin", content)))
            .OpenAsync(Endpoint(["data.bin"], new DataFormat(format)));

        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task OpenAsync_reads_multiple_common_log_paths()
    {
        var resolver = new Resolver(("one.clf", CommonLine), ("two.clf", CommonLine.Replace(" 200 ", " 404 ")));
        using var reader = await Factory(resolver).OpenAsync(Endpoint(["one.clf", "two.clf"]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["StatusCode"], Is.EqualTo(200));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["StatusCode"], Is.EqualTo(404));
    }

    [Test]
    public async Task Disposing_reader_disposes_the_source_stream()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes(CommonLine));
        var reader = await Factory(new SingleStreamResolver(stream))
            .OpenAsync(Endpoint(["data.clf"]));

        reader.Dispose();

        Assert.That(stream.Disposed, Is.True);
    }

    private static ResourceReaderFactory Factory(IEndpointStreamResolver resolver) =>
        ResourceReaderFactory.Create(options => options.AddWebLogs(), resolver);
    private static DataEndpoint Endpoint(IReadOnlyList<string> paths, DataFormat? format = null) =>
        new("data", "data", EndpointKind.File, null, new PathLocation(paths), format);

    private sealed class Resolver(params (string Path, string Content)[] values) : IEndpointStreamResolver
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(value => value.Path, value => value.Content);
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(_values[path])));
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
