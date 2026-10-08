using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;
using PocketCsvReader.WebLogs;
using PocketCsvReader.WebLogs.Configuration;

namespace Packata.ResourceReaders.WebLogs.Providers;

internal sealed class WebLogReaderProvider : IDataEndpointReaderProvider
{
    public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format) =>
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
        var resource = Resource(context);
        var schema = Schema(context.Schema);
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
