using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders;

internal interface IDataEndpointReaderProvider
{
    bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format);

    ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default);
}

internal sealed record ReaderOpenContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format,
    IReadOnlyList<Stream> Streams)
{
    public DataEndpoint Endpoint => Request.Endpoint;
    public DataSchema? Schema => Request.Schema;
}

internal sealed record ResolvedDataFormat(
    string Name,
    string? MediaType,
    string? Compression);
