using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace Packata.ResourceReaders.Ndjson.Providers;

internal sealed class NdjsonReaderProvider : IDataEndpointReaderProvider
{
    public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "ndjson" or "jsonl";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithResource(Resource(context));
        var schema = Schema(context.Schema);
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

    private static ResourceDescriptorBuilder Resource(ReaderOpenContext context)
    {
        var resource = new ResourceDescriptorBuilder();
        if (!string.IsNullOrWhiteSpace(context.Endpoint.Format?.Encoding))
            resource.WithEncoding(context.Endpoint.Format.Encoding);
        if (!string.IsNullOrWhiteSpace(context.Format.Compression))
            resource.WithCompression(context.Format.Compression);
        return resource;
    }

    private static ISchemaDescriptorBuilder? Schema(DataSchema? schema)
    {
        if (schema is not { Fields.Count: > 0 }) return null;
        var builder = new SchemaDescriptorBuilder().Named();
        var mapper = new RuntimeTypeMapper();
        foreach (var field in schema.Fields)
            builder.WithField(mapper.Map(field.LogicalType, field.Format), field.Name,
                value => field.LogicalType is null ? value : value.WithDataSourceTypeName(field.LogicalType));
        return builder;
    }
}
