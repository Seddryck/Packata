using System.Reflection;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class CanonicalSpreadsheetAndParquetTests
{
    [TestCase("application/vnd.ms-excel", "xls", "my-book.xlsx")]
    [TestCase("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx", "my-book.xlsx")]
    [TestCase("application/vnd.apache.parquet", "parquet", "iris.parquet")]
    public async Task OpenAsync_infers_binary_format_from_media_type(string mediaType, string expectedFormat,
        string resource)
    {
        var resolver = new ResourceResolver(("data.bin", resource));
        var endpoint = Endpoint(["data.bin"], new DataFormat(null, mediaType));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(endpoint);

        Assert.That(reader.Read(), Is.True, expectedFormat);
    }

    [Test]
    public async Task OpenAsync_reads_parquet_values()
    {
        var resolver = new ResourceResolver(("iris.parquet", "iris.parquet"));
        var endpoint = Endpoint(["iris.parquet"], new DataFormat("parquet"));

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(endpoint);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader[0], Is.EqualTo(5.10));
            Assert.That(reader["Species"], Is.EqualTo("setosa"));
            Assert.That(reader.GetString(4), Is.EqualTo("setosa"));
            Assert.That(reader.GetFieldType(4), Is.EqualTo(typeof(string)));
        });
    }

    [Test]
    public async Task OpenAsync_reads_named_spreadsheet_sheet_and_headers()
    {
        var resolver = new ResourceResolver(("my-book.xlsx", "my-book.xlsx"));
        var format = new DataFormat("xlsx", Options: new Dictionary<string, object?>
        {
            ["sheetName"] = "Country", ["header"] = true
        });

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(Endpoint(["my-book.xlsx"], format));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("Code"));
            Assert.That(reader["Name"], Is.EqualTo("Belgium"));
            Assert.That(reader["Capital"], Is.EqualTo("Brussels"));
        });
    }

    [Test]
    public async Task OpenAsync_reads_spreadsheet_sheet_by_number_without_header()
    {
        var resolver = new ResourceResolver(("my-book.xlsx", "my-book.xlsx"));
        var format = new DataFormat("xlsx", Options: new Dictionary<string, object?>
        {
            ["sheetNumber"] = 2, ["header"] = false
        });

        using var reader = await new ResourceReaderFactory(resolver).OpenAsync(Endpoint(["my-book.xlsx"], format));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader[0], Is.EqualTo("Code"));
    }

    [TestCase("Unknown", null)]
    [TestCase(null, 3)]
    [TestCase(null, 4)]
    public void OpenAsync_rejects_missing_spreadsheet_sheet(string? sheetName, int? sheetNumber)
    {
        var options = new Dictionary<string, object?>();
        if (sheetName is not null) options["sheetName"] = sheetName;
        if (sheetNumber is not null) options["sheetNumber"] = sheetNumber.Value;
        var endpoint = Endpoint(["my-book.xlsx"], new DataFormat("xlsx", Options: options));

        Assert.That(async () => await new ResourceReaderFactory(
                new ResourceResolver(("my-book.xlsx", "my-book.xlsx"))).OpenAsync(endpoint),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void OpenAsync_rejects_sheet_name_and_number_together()
    {
        var format = new DataFormat("xlsx", Options: new Dictionary<string, object?>
        {
            ["sheetName"] = "Country", ["sheetNumber"] = 2
        });

        Assert.That(async () => await new ResourceReaderFactory(
                new ResourceResolver(("my-book.xlsx", "my-book.xlsx"))).OpenAsync(Endpoint(["my-book.xlsx"], format)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OpenAsync_rejects_multiple_spreadsheet_paths()
    {
        var resolver = new ResourceResolver(("one.xlsx", "my-book.xlsx"), ("two.xlsx", "my-book.xlsx"));

        Assert.That(async () => await new ResourceReaderFactory(resolver)
                .OpenAsync(Endpoint(["one.xlsx", "two.xlsx"], new DataFormat("xlsx"))),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static DataEndpoint Endpoint(IReadOnlyList<string> paths, DataFormat format) =>
        new("data", "data", EndpointKind.File, null, new PathLocation(paths), format);

    private sealed class ResourceResolver(params (string Path, string Resource)[] values) : IEndpointStreamResolver
    {
        private readonly Dictionary<string, string> _values = values.ToDictionary(x => x.Path, x => x.Resource);
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                $"Packata.ResourceReaders.Testing.Resources.{_values[path]}") ?? throw new FileNotFoundException(path);
            var output = new MemoryStream(); input.CopyTo(output); output.Position = 0;
            return ValueTask.FromResult<Stream>(output);
        }
    }
}
