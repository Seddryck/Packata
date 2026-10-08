using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class ProviderDispatchTests
{
    [Test]
    public async Task Create_uses_custom_provider_before_built_in_readers()
    {
        var resolver = new TrackingResolver();
        var custom = new StubProvider(true, resolver);
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.csv"]), new DataFormat("csv"));
        var factory = ResourceReaderFactory.Create(options => options.AddProvider(custom), resolver);

        using var reader = await factory.OpenAsync(endpoint);

        Assert.That(custom.OpenCount, Is.EqualTo(1));
    }

    [Test]
    public void AddProvider_rejects_duplicate_provider_types()
    {
        var resolver = new TrackingResolver();
        var options = new ResourceReaderFactoryOptions().AddProvider(new StubProvider(true, resolver));

        Assert.That(() => options.AddProvider(new StubProvider(false, resolver)),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void OpenAsync_honors_cancellation_before_matching_custom_providers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = new TrackingResolver();
        var custom = new StubProvider(true, resolver);
        var factory = ResourceReaderFactory.Create(options => options.AddProvider(custom), resolver);
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation(["data.custom"]), new DataFormat("custom"));

        Assert.That(async () => await factory.OpenAsync(endpoint, cancellationToken: cancellation.Token),
            Throws.TypeOf<OperationCanceledException>());
        Assert.That(custom.MatchCount, Is.Zero);
    }

    [Test]
    public async Task Custom_provider_can_serve_concurrent_requests()
    {
        var provider = new ConcurrentProvider();
        var factory = ResourceReaderFactory.Create(options => options.AddProvider(provider), new TrackingResolver());
        var endpoints = new[] { "one", "two" }.Select(id => new DataEndpoint(id, id, EndpointKind.File, null,
            new PathLocation([$"{id}.custom"]), new DataFormat("custom")));

        var readers = await Task.WhenAll(endpoints.Select(endpoint => factory.OpenAsync(endpoint).AsTask()));
        foreach (var reader in readers) reader.Dispose();

        Assert.That(provider.OpenCount, Is.EqualTo(2));
    }

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

        public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format)
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

    private sealed class ConcurrentProvider : IDataEndpointReaderProvider
    {
        private int _openCount;
        public int OpenCount => _openCount;

        public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) => format.Name == "custom";

        public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openCount);
            return ValueTask.FromResult<IDataReader>(new DataTable().CreateDataReader());
        }
    }
}
