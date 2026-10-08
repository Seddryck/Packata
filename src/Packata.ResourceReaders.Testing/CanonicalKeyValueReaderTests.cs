using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.KeyValue;

namespace Packata.ResourceReaders.Testing;

public class CanonicalKeyValueReaderTests
{
    [TestCase("ltsv", null, "id:1\tname:Ada")]
    [TestCase(null, "text/x-ltsv", "id:1\tname:Ada")]
    [TestCase("logfmt", null, "id=1 name=Ada")]
    [TestCase("log-fmt", null, "id=1 name=Ada")]
    [TestCase(null, "application/logfmt", "id=1 name=Ada")]
    public async Task OpenAsync_reads_supported_names_and_media_types(string? name, string? mediaType, string content)
    {
        using var reader = await Factory(new Resolver(("data.bin", content)))
            .OpenAsync(Endpoint(["data.bin"], new DataFormat(name, mediaType)));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("Ada"));
    }

    [Test]
    public async Task OpenAsync_applies_schema_order_types_and_missing_keys()
    {
        var schema = new DataSchema([new DataField("id", "integer"), new DataField("name", "string")]);
        using var reader = await Factory(new Resolver(("data.ltsv", "name:Ada\tid:1\nid:2")))
            .OpenAsync(Endpoint(["data.ltsv"]), schema);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("id"));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
            Assert.That(reader["id"], Is.EqualTo(1));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(1), Is.True);
    }

    [Test]
    public async Task OpenAsync_keeps_first_record_columns_and_duplicate_keys_without_schema()
    {
        const string content = "tag=first tag=second level=info\nlevel=warn extra=ignored";
        using var reader = await Factory(new Resolver(("data.logfmt", content))).OpenAsync(Endpoint(["data.logfmt"]));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(0), Is.EqualTo("tag"));
            Assert.That(reader.GetName(1), Is.EqualTo("tag"));
            Assert.That(reader["tag"], Is.EqualTo("first"));
            Assert.That(reader[1], Is.EqualTo("second"));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader["level"], Is.EqualTo("warn"));
            Assert.That(reader.FieldCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task OpenAsync_honors_logfmt_quoting_and_reads_multiple_paths()
    {
        var resolver = new Resolver(
            ("one.logfmt", "message=\"hello \\\"Ada\\\"\""),
            ("two.logfmt", "message=ready"));
        var schema = new DataSchema([new DataField("message", "string")]);
        using var reader = await Factory(resolver).OpenAsync(Endpoint(["one.logfmt", "two.logfmt"]), schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["message"], Is.EqualTo("hello \"Ada\""));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["message"], Is.EqualTo("ready"));
    }

    [Test]
    public async Task OpenAsync_keeps_first_path_columns_across_paths_without_a_schema()
    {
        var resolver = new Resolver(("one.logfmt", "level=info message=ready"),
            ("two.logfmt", "level=warn extra=ignored"));
        using var reader = await Factory(resolver).OpenAsync(Endpoint(["one.logfmt", "two.logfmt"]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(2));
            Assert.That(reader["level"], Is.EqualTo("warn"));
            Assert.That(reader.IsDBNull(reader.GetOrdinal("message")), Is.True);
        });
    }

    [Test]
    public async Task Disposing_reader_disposes_the_source_stream()
    {
        var stream = new TrackingStream(Encoding.UTF8.GetBytes("id:1"));
        var reader = await Factory(new SingleStreamResolver(stream)).OpenAsync(Endpoint(["data.ltsv"]));

        reader.Dispose();

        Assert.That(stream.Disposed, Is.True);
    }

    private static ResourceReaderFactory Factory(IEndpointStreamResolver resolver) =>
        ResourceReaderFactory.Create(options => options.AddKeyValueReaders(), resolver);
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
