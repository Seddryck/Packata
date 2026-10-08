using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders;

/// <summary>Opens canonical endpoints matched by a resource reader provider.</summary>
/// <remarks>
/// Implementations must be safe for concurrent calls. Streams supplied through <see cref="ReaderOpenContext"/>
/// remain owned by the factory and must not be disposed by the provider. Any additional resources opened by a
/// provider must be owned by the returned reader and released when that reader is disposed.
/// </remarks>
public interface IDataEndpointReaderProvider
{
    /// <summary>Returns whether this provider can open the request. Matching must not acquire resources.</summary>
    bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format);

    /// <summary>Opens a reader for a previously matched request.</summary>
    ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Supplies a provider with the canonical request, resolved format, and factory-owned streams.</summary>
public sealed record ReaderOpenContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format,
    IReadOnlyList<Stream> Streams)
{
    public DataEndpoint Endpoint => Request.Endpoint;
    public DataSchema? Schema => Request.Schema;
}

/// <summary>Contains normalized format information resolved by the reader factory.</summary>
public sealed record ResolvedDataFormat(
    string Name,
    string? MediaType,
    string? Compression);
