using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Parquet.Providers;

internal sealed class ParquetReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "parquet" or "pqt";

    public async ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default) =>
        await Tabular.ParquetDataReader.CreateAsync(context.Streams).ConfigureAwait(false);
}
