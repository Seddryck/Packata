using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;

namespace Packata.ResourceReaders.KeyValue.Providers;

internal sealed class KeyValueReaderProvider : IDataEndpointReaderProvider
{
    public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format) =>
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
        var resource = Resource(context);
        var schema = Schema(context.Schema);
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
