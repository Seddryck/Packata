using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Providers;

internal sealed class InlineReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is InlineLocation;

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        var endpoint = context.Endpoint;
        var location = (InlineLocation)endpoint.Location;
        try
        {
            var table = Tabular.InlineDataReader.CreateTable(location.Value, context.Schema);
            return ValueTask.FromResult<IDataReader>(new OwnedDataReader(table.CreateDataReader(), table));
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Inline endpoint '{endpoint.Id}' is invalid: {exception.Message}",
                nameof(endpoint), exception);
        }
    }
}
