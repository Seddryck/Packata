using System.Data;
using System.Data.Common;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Excel;
using Packata.ResourceReaders.Parquet;

namespace Packata.Cli;

internal sealed record PreviewSample(string[] Columns, IReadOnlyList<object?[]> Rows);

internal static class PreviewService
{
    public static IDataEndpointReaderFactory CreateFactory(Uri baseUri) =>
        ResourceReaderFactory.Create(options => options.AddExcel().AddParquet()
            .AddDatabase(baseUri.IsFile ? baseUri.LocalPath : string.Empty), new RelativeStreamResolver(baseUri));

    public static DataEndpointReadRequest Request(DataContract contract, DataAsset asset)
    {
        if (asset.EndpointBindings.Count != 1)
            throw new ContractOperationException("CLI020", "$.assets.endpointBindings",
                $"Asset '{asset.Id}' must have exactly one endpoint binding for preview; none or multiple bindings cannot be selected automatically.");
        var binding = asset.EndpointBindings[0];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1)
            throw new ContractOperationException("CLI021", "$.endpoints", $"Endpoint '{binding.EndpointId}' for asset '{asset.Id}' could not be resolved uniquely.");
        return new(endpoints[0], asset.Schema, binding.AssetPath ?? asset.PhysicalName ?? asset.Name);
    }

    public static async Task<PreviewSample> ReadAsync(IDataEndpointReaderFactory factory, DataEndpointReadRequest request,
        int limit, string? columns, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = await factory.OpenAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // Some streaming readers discover their headers/schema only on the first Read.
        var hasRow = await ReadNextAsync(reader, cancellationToken).ConfigureAwait(false);
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        if (!hasRow && names.Length == 0 && request.Schema is { Fields.Count: > 0 } schema)
            names = schema.Fields.Select(field => field.Name).ToArray();
        var selected = columns is null ? names : columns.Split(',', StringSplitOptions.TrimEntries);
        var ordinals = selected.Select(name =>
        {
            var matches = Enumerable.Range(0, names.Length).Where(index => names[index] == name).ToArray();
            return matches.Length == 1 ? matches[0] : throw new ContractOperationException("CLI022", "$.columns",
                $"Column '{name}' is unknown or ambiguous. Select exact, unique field names.");
        }).ToArray();
        var rows = new List<object?[]>();
        while (hasRow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(ordinals.Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            if (rows.Count == limit) break;
            hasRow = await ReadNextAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        return new(selected, rows);
    }

    private static async ValueTask<bool> ReadNextAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = reader is DbDataReader asyncReader
            ? await asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false)
            : reader.Read();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private sealed class RelativeStreamResolver(Uri baseUri) : IEndpointStreamResolver
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
