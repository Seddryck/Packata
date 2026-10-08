using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;

namespace Packata.ResourceReaders.KeyValue.Providers;

internal sealed class KeyValueReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "ltsv" or "logfmt" or "log-fmt";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reader = BuildReader(context);
        IDataReader Open(Stream stream) => reader.ToDataReader(stream);
        IDataReader source = context.Streams.Count == 1
            ? Open(context.Streams[0])
            : new SequentialDataReader(context.Streams.Select(Open));
        IDataReader dataReader = context.Schema is null
            ? new DiscoveredSchemaDataReader(source)
            : new NamedSchemaDataReader(source, context.Schema);
        return ValueTask.FromResult(dataReader);
    }

    private static KeyValueReader BuildReader(ReaderOpenContext context)
    {
        var resource = PocketCsvReaderProviderDefaults.CreateResource(context);
        var schema = PocketCsvReaderProviderDefaults.CreateNamedSchema(context.Schema);
        if (context.Format.Name == "ltsv")
        {
            var builder = new LtsvReaderBuilder().WithResource(resource);
            if (schema is not null) builder.WithSchema(schema);
            return builder.Build();
        }
        else
        {
            var builder = new LogfmtReaderBuilder().WithResource(resource);
            if (schema is not null) builder.WithSchema(schema);
            return builder.Build();
        }
    }
}
