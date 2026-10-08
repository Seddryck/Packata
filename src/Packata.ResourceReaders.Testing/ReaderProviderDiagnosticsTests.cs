using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class ReaderProviderDiagnosticsTests
{
    [TestCase("xlsx", null, "xlsx", "Packata.ResourceReaders.Excel", "AddExcel()")]
    [TestCase("xls", null, "xlsx", "Packata.ResourceReaders.Excel", "AddExcel()")]
    [TestCase("pqt", null, "parquet", "Packata.ResourceReaders.Parquet", "AddParquet()")]
    [TestCase(null, "application/vnd.apache.parquet", "parquet", "Packata.ResourceReaders.Parquet", "AddParquet()")]
    public async Task OpenAsync_reports_the_missing_optional_provider(string? name, string? mediaType,
        string canonical, string package, string registration)
    {
        var resolver = new TrackingResolver();
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.bin"]), new DataFormat(name, mediaType));

        var exception = await Assert.ThrowsAsync<ReaderProviderUnavailableException>(async () =>
            await new ResourceReaderFactory(resolver).OpenAsync(endpoint));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Format, Is.EqualTo(canonical));
            Assert.That(exception.SuggestedPackage, Is.EqualTo(package));
            Assert.That(exception.RegistrationMethod, Is.EqualTo(registration));
            Assert.That(resolver.OpenCount, Is.Zero);
        });
    }

    [Test]
    public async Task OpenAsync_reports_the_missing_database_provider()
    {
        var endpoint = new DataEndpoint("data", "data", EndpointKind.Database, null,
            new ConnectionLocation("mssql", ConnectionUrl: "mssql://server/database"), new DataFormat("database"));

        var exception = await Assert.ThrowsAsync<ReaderProviderUnavailableException>(async () =>
            await new ResourceReaderFactory().OpenAsync(endpoint));

        Assert.That(exception!.SuggestedPackage, Is.EqualTo("Packata.ResourceReaders.Database"));
    }

    [Test]
    public async Task OpenAsync_keeps_unknown_formats_on_the_generic_error_path()
    {
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.unknown"]), new DataFormat("unknown"));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await new ResourceReaderFactory(new TrackingResolver()).OpenAsync(endpoint));

        Assert.That(exception, Is.Not.TypeOf<ReaderProviderUnavailableException>());
    }

    private sealed class TrackingResolver : IEndpointStreamResolver
    {
        public int OpenCount { get; private set; }
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return ValueTask.FromResult<Stream>(new MemoryStream());
        }
    }
}
