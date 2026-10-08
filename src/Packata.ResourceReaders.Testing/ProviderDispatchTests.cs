using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class ProviderDispatchTests
{
    [Test]
    public async Task OpenAsync_selects_provider_before_opening_paths()
    {
        var resolver = new TrackingResolver();
        var skipped = new StubProvider(false, resolver);
        var selected = new StubProvider(true, resolver);
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.custom"]), new DataFormat("custom"));
        var factory = new ResourceReaderFactory(resolver, [skipped, selected]);

        using var reader = await factory.OpenAsync(endpoint);

        Assert.Multiple(() =>
        {
            Assert.That(skipped.MatchCount, Is.EqualTo(1));
            Assert.That(skipped.OpenCount, Is.Zero);
            Assert.That(selected.MatchCount, Is.EqualTo(1));
            Assert.That(selected.OpenCount, Is.EqualTo(1));
            Assert.That(resolver.OpenCount, Is.EqualTo(1));
        });
    }

    private sealed class StubProvider(bool matches, TrackingResolver resolver) : IDataEndpointReaderProvider
    {
        public int MatchCount { get; private set; }
        public int OpenCount { get; private set; }

        public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format)
        {
            MatchCount++;
            Assert.That(resolver.OpenCount, Is.Zero);
            return matches;
        }

        public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            var table = new DataTable();
            table.Columns.Add("value");
            return ValueTask.FromResult<IDataReader>(table.CreateDataReader());
        }
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
