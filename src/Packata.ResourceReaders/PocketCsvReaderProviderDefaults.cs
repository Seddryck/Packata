using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal static class PocketCsvReaderProviderDefaults
{
    private static readonly DefaultFormatMapper DefaultFormats = new();
    public static ResourceDescriptorBuilder CreateResource(ReaderOpenContext context)
    {
        var resource = new ResourceDescriptorBuilder();
        if (!string.IsNullOrWhiteSpace(context.Endpoint.Format?.Encoding))
            resource.WithEncoding(context.Endpoint.Format.Encoding);
        if (!string.IsNullOrWhiteSpace(context.Format.Compression))
            resource.WithCompression(context.Format.Compression);
        return resource;
    }

    public static ISchemaDescriptorBuilder? CreateNamedSchema(DataSchema? schema)
    {
        if (schema is not { Fields.Count: > 0 }) return null;
        var builder = new SchemaDescriptorBuilder().Named();
        var mapper = new RuntimeTypeMapper();
        foreach (var field in schema.Fields)
            AddField(builder, mapper, field);
        return builder;
    }

    internal static void AddField(ISchemaDescriptorBuilder schema, RuntimeTypeMapper mapper, DataField field)
    {
        var type = mapper.Map(field.LogicalType, field.Format);
        var pattern = field.Format;
        if ((pattern is null || pattern.Equals("default", StringComparison.OrdinalIgnoreCase))
            && field.LogicalType is not null)
            DefaultFormats.TryGetMapping(field.LogicalType, out pattern);

        if ((type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly))
            && !string.IsNullOrEmpty(pattern) && pattern != "any")
        {
            schema.WithTemporalField(type, field.Name, builder =>
            {
                builder.WithFormat(DateTimeFormatConverter.Convert(pattern));
                if (field.LogicalType is not null) builder.WithDataSourceTypeName(field.LogicalType);
                return builder;
            });
        }
        else
            schema.WithField(type, field.Name,
                builder => field.LogicalType is null ? builder : builder.WithDataSourceTypeName(field.LogicalType));
    }
}
