using System.Data;
using Packata.Core.Contracts;

namespace Packata.Core.Reading;

/// <summary>Opens tabular data from a canonical endpoint without retaining request state.</summary>
public interface IDataEndpointReaderFactory
{
    ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
        CancellationToken cancellationToken = default);

    ValueTask<IDataReader> OpenAsync(DataEndpointReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OpenAsync(request.Endpoint, request.Schema, cancellationToken);
    }
}

/// <summary>Describes an endpoint read together with asset-specific context.</summary>
public sealed record DataEndpointReadRequest(
    DataEndpoint Endpoint,
    DataSchema? Schema = null,
    string? AssetPath = null);

/// <summary>Resolves canonical path values to owned streams.</summary>
public interface IEndpointStreamResolver
{
    ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default);
}
