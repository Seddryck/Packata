using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Ndjson.Configuration;

namespace Packata.ResourceReaders.Ndjson.Providers;

internal sealed class NdjsonReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "ndjson" or "jsonl";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithResource(PocketCsvReaderProviderDefaults.CreateResource(context));
        var schema = PocketCsvReaderProviderDefaults.CreateNamedSchema(context.Schema);
        if (schema is not null) builder.WithSchema(schema);
        var reader = builder.Build();
        IDataReader Open(Stream stream)
        {
            IDataReader source = reader.ToDataReader(stream);
            return context.Schema is null ? source : new NamedSchemaDataReader(source, context.Schema);
        }
        IDataReader dataReader = context.Streams.Count == 1
            ? Open(context.Streams[0])
            : new SequentialDataReader(context.Streams.Select(Open));
        return ValueTask.FromResult(dataReader);
    }
}
