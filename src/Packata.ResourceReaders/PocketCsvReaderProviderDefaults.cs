using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal static class PocketCsvReaderProviderDefaults
{
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
            builder.WithField(mapper.Map(field.LogicalType, field.Format), field.Name,
                value => field.LogicalType is null ? value : value.WithDataSourceTypeName(field.LogicalType));
        return builder;
    }
}
