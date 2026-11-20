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
    private readonly CategoryDecodingOptions _categories;

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
            options.CombineWith(DefaultProviders()), options.Formats, CategoryDecodingOptions.From(options));
    }

    internal ResourceReaderFactory(IEndpointStreamResolver? streams,
        IEnumerable<IDataEndpointReaderProvider> providers)
        : this(streams, providers, new DataFormatResolutionOptions())
    { }

    private ResourceReaderFactory(IEndpointStreamResolver? streams,
        IEnumerable<IDataEndpointReaderProvider> providers,
        DataFormatResolutionOptions formatOptions, CategoryDecodingOptions? categories = null)
    {
        _streams = streams ?? new DefaultEndpointStreamResolver();
        _providers = providers.ToArray();
        _categories = categories ?? new();
        foreach (var provider in _providers.OfType<DelimitedReaderProvider>())
            provider.DecodeCategories = _categories.Enabled;
        _formats = new DataFormatResolver(formatOptions);
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
        if (request.IndexedRead is { } indexed)
        {
            if (provider is not IDataEndpointIndexedReaderProvider indexedProvider)
                throw new NotSupportedException($"The reader for endpoint '{endpoint.Id}' does not support indexed lookups.");
            var equalities = ResolveEqualities(request.Schema, indexed);
            if (indexed.PartitionFilter is { } keep)
            {
                if (endpoint.Location is not PathLocation partitions)
                    throw new NotSupportedException("Path partition filtering requires a path-based endpoint.");
                request = request with { Endpoint = endpoint with { Location = new PathLocation(partitions.Paths.Where(keep).ToArray()) } };
            }

            return await indexedProvider.OpenIndexedAsync(new(request, format, _streams, equalities), cancellationToken)
                .ConfigureAwait(false);
        }

        if (_categories.Enabled && provider is not DelimitedReaderProvider)
            throw new CategoryDecodingNotSupportedException();
        if (provider is null) throw UnsupportedEndpoint(endpoint, format);

        if (_categories.Enabled)
        {
            request = await new CategoryTableLoader(_streams, _formats, _providers, _categories)
                .ExpandAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var opened = new List<Stream>();
        try
        {
            if (endpoint.Location is PathLocation paths)
            {
                if (paths.Paths.Count == 0)
                {
                    throw new NotSupportedException(
                        $"Endpoint '{endpoint.Id}' does not expose readable paths, inline data, or a connection.");
                }

                foreach (var path in paths.Paths)
                    opened.Add(await _streams.OpenAsync(path, cancellationToken).ConfigureAwait(false));
            }

            var context = new ReaderOpenContext(request, format, opened);
            var reader = await provider.OpenAsync(context, cancellationToken).ConfigureAwait(false);
            return new OwnedDataReader(reader, opened.Cast<IDisposable>().ToArray());
        }
        catch
        {
            ResourceDisposal.Run(opened.Select(stream => (Action)stream.Dispose), suppressFailures: true);
            throw;
        }
    }

    private static IReadOnlyList<ResolvedIndexedEquality> ResolveEqualities(DataSchema? schema, IndexedRead read)
    {
        if (schema is null || read.Equalities is not { Count: > 0 })
            throw new ArgumentException("An indexed lookup requires a schema and at least one equality.", nameof(read));

        var result = new List<ResolvedIndexedEquality>();
        foreach (var equality in read.Equalities)
        {
            ArgumentNullException.ThrowIfNull(equality);
            ArgumentNullException.ThrowIfNull(equality.Value);
            var indexes = schema.Indexes.Where(index => index.Property == equality.Property && index.Kind == equality.Kind).ToArray();
            var fields = schema.Fields.Select((field, ordinal) => (field, ordinal))
                .Where(item => item.field.Name == equality.Property).ToArray();
            if (indexes.Length != 1 || fields.Length != 1)
                throw new ArgumentException($"Expected one declared {equality.Kind} index on schema property '{equality.Property}'.", nameof(read));

            result.Add(new(indexes[0], fields[0].field, fields[0].ordinal, equality.Value));
        }

        return result.AsReadOnly();
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
