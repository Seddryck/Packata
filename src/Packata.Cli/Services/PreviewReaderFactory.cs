using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Excel;
using Packata.ResourceReaders.Parquet;

namespace Packata.Cli;

internal sealed class PreviewReaderFactory(Uri baseUri, IDatabaseConnectionService? connections,
    IEndpointStreamResolver streams, bool decodeCategories, long stagingMaximumBytes) : IDataEndpointReaderFactory
{
    public ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
        CancellationToken cancellationToken = default) => OpenAsync(new DataEndpointReadRequest(endpoint, schema), cancellationToken);

    public ValueTask<IDataReader> OpenAsync(DataEndpointReadRequest request, CancellationToken cancellationToken = default)
    {
        var format = new DataFormatResolver().Resolve(request.Endpoint);
        var acquisition = new ResourceAcquisition(streams, format, stagingMaximumBytes);
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.DecodeCategories = decodeCategories;
            options.AddExcel().AddParquet();
            if (connections is null) options.AddDatabase(baseUri.IsFile ? baseUri.LocalPath : string.Empty);
            else options.AddDatabase(connections);
        }, acquisition);
        return factory.OpenAsync(request, cancellationToken);
    }
}
