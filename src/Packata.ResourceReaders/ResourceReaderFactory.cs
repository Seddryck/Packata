using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Providers;

namespace Packata.ResourceReaders;

/// <summary>
/// Concurrent-safe reader entry point for canonical endpoints. A returned reader owns the streams,
/// commands, and connections opened for it and releases them when disposed.
/// Inline endpoints accept enumerable rows represented by string-keyed dictionaries, or positional
/// enumerable rows when a schema supplies the column names.
/// </summary>
public sealed class ResourceReaderFactory : IDataEndpointReaderFactory
{
    private readonly IEndpointStreamResolver _streams;
    private readonly IReadOnlyList<IDataEndpointReaderProvider> _providers;

    public ResourceReaderFactory(IEndpointStreamResolver? streams = null)
        : this(streams, DefaultProviders())
    { }

    /// <summary>Creates a factory with explicitly registered providers evaluated before the built-in readers.</summary>
    public static ResourceReaderFactory Create(Action<ResourceReaderFactoryOptions> configure,
        IEndpointStreamResolver? streams = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new ResourceReaderFactoryOptions();
        configure(options);
        return new ResourceReaderFactory(streams,
            options.CombineWith(DefaultProviders()));
    }

    internal ResourceReaderFactory(IEndpointStreamResolver? streams,
        IEnumerable<IDataEndpointReaderProvider> providers)
    {
        _streams = streams ?? new DefaultEndpointStreamResolver();
        _providers = providers.ToArray();
    }

    public async ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
        CancellationToken cancellationToken = default)
        => await OpenAsync(new DataEndpointReadRequest(endpoint, schema), cancellationToken).ConfigureAwait(false);

    public async ValueTask<IDataReader> OpenAsync(DataEndpointReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var endpoint = request.Endpoint;
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var format = DataFormatResolver.Resolve(endpoint);
        var provider = _providers.FirstOrDefault(candidate => candidate.CanOpen(request, format))
            ?? throw UnsupportedEndpoint(endpoint, format);

        var opened = new List<Stream>();
        try
        {
            if (endpoint.Location is PathLocation paths)
            {
                if (paths.Paths.Count == 0)
                    throw new NotSupportedException(
                        $"Endpoint '{endpoint.Id}' does not expose readable paths, inline data, or a connection.");
                foreach (var path in paths.Paths)
                    opened.Add(await _streams.OpenAsync(path, cancellationToken).ConfigureAwait(false));
            }

            var context = new ReaderOpenContext(request, format, opened);
            var reader = await provider.OpenAsync(context, cancellationToken).ConfigureAwait(false);
            return new OwnedDataReader(reader, opened.Cast<IDisposable>().ToArray());
        }
        catch
        {
            foreach (var stream in opened) stream.Dispose();
            throw;
        }
    }

    private static IDataEndpointReaderProvider[] DefaultProviders() =>
    [
        new InlineReaderProvider(),
        new DelimitedReaderProvider()
    ];

    private static NotSupportedException UnsupportedEndpoint(DataEndpoint endpoint, ResolvedDataFormat format) =>
        endpoint.Location is PathLocation
            ? new NotSupportedException(string.IsNullOrEmpty(format.Name)
                ? $"The format of endpoint '{endpoint.Id}' could not be determined."
                : $"Resource format '{format.Name}' is not supported for endpoint '{endpoint.Id}'.")
            : new NotSupportedException(
                $"Endpoint '{endpoint.Id}' does not expose readable paths, inline data, or a connection.");
}
