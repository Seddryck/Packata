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
    private readonly DataFormatResolver _formats;
    private readonly bool _decodeCategories;
    private readonly int _categoryTableRowLimit;
    private readonly long _categoryTableByteLimit;

    public ResourceReaderFactory(IEndpointStreamResolver? streams = null)
        : this(streams, DefaultProviders(), new DataFormatResolutionOptions())
    { }

    /// <summary>Creates a factory with explicitly registered providers evaluated before the built-in readers.</summary>
    public static ResourceReaderFactory Create(Action<ResourceReaderFactoryOptions> configure,
        IEndpointStreamResolver? streams = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new ResourceReaderFactoryOptions();
        configure(options);
        return new ResourceReaderFactory(streams,
            options.CombineWith(DefaultProviders()), options.Formats, options.DecodeCategories,
            options.CategoryTableRowLimit, options.CategoryTableByteLimit);
    }

    internal ResourceReaderFactory(IEndpointStreamResolver? streams,
        IEnumerable<IDataEndpointReaderProvider> providers)
        : this(streams, providers, new DataFormatResolutionOptions())
    { }

    private ResourceReaderFactory(IEndpointStreamResolver? streams,
        IEnumerable<IDataEndpointReaderProvider> providers,
        DataFormatResolutionOptions formatOptions, bool decodeCategories = false,
        int categoryTableRowLimit = 100_000, long categoryTableByteLimit = 16 * 1024 * 1024)
    {
        _streams = streams ?? new DefaultEndpointStreamResolver();
        _providers = providers.ToArray();
        foreach (var provider in _providers.OfType<DelimitedReaderProvider>())
            provider.DecodeCategories = decodeCategories;
        _formats = new DataFormatResolver(formatOptions);
        _decodeCategories = decodeCategories;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryTableRowLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryTableByteLimit);
        _categoryTableRowLimit = categoryTableRowLimit;
        _categoryTableByteLimit = categoryTableByteLimit;
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

        var format = _formats.Resolve(endpoint);
        var provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(request, format));
        if (_decodeCategories && provider is not DelimitedReaderProvider)
            throw new CategoryDecodingNotSupportedException();
        if (provider is null) throw UnsupportedEndpoint(endpoint, format);

        if (_decodeCategories)
            request = await new CategoryTableLoader(_streams, _formats, _providers,
                _categoryTableRowLimit, _categoryTableByteLimit).ExpandAsync(request, cancellationToken).ConfigureAwait(false);

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
        KnownFormatRegistry.Find(endpoint, format) is { } known
            ? new ReaderProviderUnavailableException(known)
            : endpoint.Location is PathLocation
                ? new NotSupportedException(string.IsNullOrEmpty(format.Name)
                ? $"The format of endpoint '{endpoint.Id}' could not be determined."
                : $"Resource format '{format.Name}' is not supported for endpoint '{endpoint.Id}'.")
                : new NotSupportedException(
                    $"Endpoint '{endpoint.Id}' does not expose readable paths, inline data, or a connection.");
}
