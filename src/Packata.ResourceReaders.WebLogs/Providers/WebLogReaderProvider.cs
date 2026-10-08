using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.WebLogs;
using PocketCsvReader.WebLogs.Configuration;

namespace Packata.ResourceReaders.WebLogs.Providers;

internal sealed class WebLogReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is
            "common-log" or "commonlog" or "clf" or "w3c-log" or "w3c" or "w3c-extended";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reader = BuildReader(context);
        IDataReader Open(Stream stream)
        {
            IDataReader source = reader.ToDataReader(stream);
            return context.Schema is null
                ? new TypedWebLogDataReader(source)
                : new NamedSchemaDataReader(source, context.Schema);
        }
        IDataReader dataReader = context.Streams.Count == 1
            ? Open(context.Streams[0])
            : new SequentialDataReader(context.Streams.Select(Open));
        return ValueTask.FromResult(dataReader);
    }

    private static WebLogReader BuildReader(ReaderOpenContext context)
    {
        var resource = PocketCsvReaderProviderDefaults.CreateResource(context);
        var schema = PocketCsvReaderProviderDefaults.CreateNamedSchema(context.Schema);
        if (context.Format.Name is "common-log" or "commonlog" or "clf")
        {
            var builder = new CommonLogReaderBuilder().WithResource(resource);
            if (schema is not null) builder.WithSchema(schema);
            return builder.Build();
        }
        else
        {
            var builder = new W3cExtendedLogReaderBuilder().WithResource(resource);
            if (schema is not null) builder.WithSchema(schema);
            return builder.Build();
        }
    }
}
