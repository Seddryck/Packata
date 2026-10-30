using Packata.Application;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders;
using Packata.Database;

namespace Packata.Cli;

internal sealed record PreviewSample(string[] Columns, IReadOnlyList<object?[]> Rows);

internal static class PreviewService
{
    public static IDataEndpointReaderFactory CreateFactory(Uri baseUri, IDatabaseConnectionService? connections = null,
        IEndpointStreamResolver? streams = null, bool decodeCategories = false,
        long stagingMaximumBytes = Packata.Storages.SeekableStaging.DefaultMaximumBytes,
        string? decodeCategoriesLanguage = null, string? decodeCategoriesLabel = null) =>
        new PreviewReaderFactory(baseUri, connections, streams ?? new RelativeStreamResolver(baseUri), decodeCategories,
            stagingMaximumBytes, decodeCategoriesLanguage, decodeCategoriesLabel);

    public static DataEndpointReadRequest Request(DataContract contract, DataAsset asset)
    {
        try { return DataPreviewService.SelectRequest(contract, asset); }
        catch (DataPreviewSelectionException error) { throw SelectionFailure(error); }
    }

    public static async Task<PreviewSample> ReadAsync(IDataEndpointReaderFactory factory, DataEndpointReadRequest request,
        int limit, string? columns, CancellationToken cancellationToken)
    {
        try
        {
            var sample = await new DataPreviewService(factory).ReadAsync(request,
                new(limit, columns?.Split(',', StringSplitOptions.TrimEntries)), cancellationToken).ConfigureAwait(false);
            return new(sample.Columns.Select(column => column.Name).ToArray(), sample.Rows);
        }
        catch (DataPreviewSelectionException error) { throw SelectionFailure(error); }
    }

    private static ContractOperationException SelectionFailure(DataPreviewSelectionException error) => error.Failure switch
    {
        DataPreviewSelectionFailure.Binding => new("CLI020", "$.assets.endpointBindings", error.Message),
        DataPreviewSelectionFailure.Endpoint => new("CLI021", "$.endpoints", error.Message),
        _ => new("CLI022", "$.columns", error.Message)
    };

    internal sealed class ArchiveStreamResolver(Packata.Core.Storage.IDocumentContainer archive, string descriptor) : IEndpointStreamResolver
    {
        private readonly DefaultEndpointStreamResolver _inner = new();
        private readonly Uri _descriptor = new(new Uri("archive:///"), descriptor);

        public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Uri.TryCreate(path, UriKind.Absolute, out _))
                return await _inner.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            var decoded = Uri.UnescapeDataString(path).Replace('\\', '/');
            var depth = _descriptor.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            if (decoded.StartsWith('/') || decoded.Contains(':') || decoded.Contains('\0'))
                throw new InvalidDataException("Invalid archive-relative resource path.");
            foreach (var part in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "..")
                {
                    if (--depth < 0) throw new InvalidDataException("Resource path escapes the archive root.");
                }
                else if (part != ".") depth++;
            }
            var entry = Uri.UnescapeDataString(new Uri(_descriptor, path).AbsolutePath).TrimStart('/');
            if (!await archive.ExistsAsync(entry).ConfigureAwait(false))
                throw new ContractOperationException("CLI023", "$.resources.path", $"Resource entry '{entry}' was not found in the archive package.");
            return await archive.OpenAsync(entry).ConfigureAwait(false);
        }
    }

    internal sealed class RelativeStreamResolver(Uri baseUri) : IEndpointStreamResolver
    {
        private readonly DefaultEndpointStreamResolver _inner = new();
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = Uri.TryCreate(path, UriKind.Absolute, out var absolute) ? absolute : new Uri(baseUri, path);
            return _inner.OpenAsync(resolved.IsFile ? resolved.LocalPath : resolved.AbsoluteUri, cancellationToken);
        }
    }
}
