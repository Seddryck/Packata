using System.Reflection;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Excel;
using Packata.ResourceReaders.Parquet;

namespace Packata.ResourceReaders.Testing;

public class CanonicalReaderFactoryTests
{
    [Test]
    public async Task OpenAsync_reads_delimited_endpoint_with_canonical_schema()
    {
        var streams = new MemoryResolver(new Dictionary<string, byte[]>
        {
            ["orders.csv"] = Encoding.UTF8.GetBytes("id;name\r\n1;alpha\r\n")
        });
        var endpoint = Endpoint("orders.csv", "csv", new Dictionary<string, object?>
        {
            ["delimiter"] = ';', ["header"] = true
        });
        var schema = new DataSchema([new DataField("id", "integer"), new DataField("name", "string")]);

        using var reader = await new ResourceReaderFactory(streams).OpenAsync(endpoint, schema);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader["id"], Is.EqualTo(1));
            Assert.That(reader["name"], Is.EqualTo("alpha"));
        });
    }

    [Test]
    public async Task OpenAsync_is_safe_for_concurrent_requests()
    {
        var streams = new MemoryResolver(new Dictionary<string, byte[]>
        {
            ["one.csv"] = Encoding.UTF8.GetBytes("value\r\none\r\n"),
            ["two.csv"] = Encoding.UTF8.GetBytes("value\r\ntwo\r\n")
        });
        var factory = new ResourceReaderFactory(streams);

        var opened = await Task.WhenAll(
            factory.OpenAsync(Endpoint("one.csv", "csv")).AsTask(),
            factory.OpenAsync(Endpoint("two.csv", "csv")).AsTask());

        using var first = opened[0]; using var second = opened[1];
        Assert.That(first.Read(), Is.True); Assert.That(second.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first["value"], Is.EqualTo("one"));
            Assert.That(second["value"], Is.EqualTo("two"));
        });
    }

    [Test]
    public void OpenAsync_honors_cancellation_before_opening()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var factory = new ResourceReaderFactory(new MemoryResolver(new Dictionary<string, byte[]>()));
        Assert.That(async () => await factory.OpenAsync(Endpoint("data.csv", "csv"), cancellationToken: cancellation.Token),
            Throws.TypeOf<OperationCanceledException>());
    }

    [TestCase("iris.parquet", "parquet")]
    [TestCase("my-book.xlsx", "xlsx")]
    public async Task OpenAsync_reads_binary_tabular_formats(string resourceName, string format)
    {
        var bytes = ReadEmbedded(resourceName);
        var resolver = new MemoryResolver(new Dictionary<string, byte[]> { [resourceName] = bytes });
        var factory = ResourceReaderFactory.Create(options =>
        {
            if (format == "xlsx") options.AddExcel();
            else options.AddParquet();
        }, resolver);
        using var reader = await factory.OpenAsync(Endpoint(resourceName, format));
        Assert.That(reader.Read(), Is.True);
    }

    private static DataEndpoint Endpoint(string path, string format,
        IReadOnlyDictionary<string, object?>? options = null) =>
        new(path, path, EndpointKind.File, null, new PathLocation([path]), new DataFormat(format, Options: options));

    private static byte[] ReadEmbedded(string name)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"Packata.ResourceReaders.Testing.Resources.{name}") ?? throw new FileNotFoundException(name);
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }

    private sealed class MemoryResolver(IReadOnlyDictionary<string, byte[]> values) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(values[path], writable: false));
        }
    }
}
