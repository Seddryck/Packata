using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader.Configuration;
using System.Globalization;

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

    internal static void AddField(ISchemaDescriptorBuilder schema, RuntimeTypeMapper mapper, DataField field, bool decodeCategories = false)
    {
        if (decodeCategories && field.ValueDomain?.Entries is { } entries && entries.Any(entry => entry.Label is not null))
        {
            // Native sequences replace text before type conversion. A labeled column is
            // therefore a string presentation column, including its fallback code text.
            schema.WithField(typeof(string), field.Name, builder =>
            {
                builder.WithDataSourceTypeName("string");
                foreach (var entry in entries.Where(entry => entry.Label is not null))
                    builder.WithSequence(SequenceText(entry.Value), entry.Label);
                return builder;
            });
            return;
        }
        var type = mapper.Map(field.LogicalType, field.Format);
        var pattern = ResolvePattern(field);
        if (IsTemporal(type) && pattern is not null)
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

    private static string SequenceText(ScalarValue scalar) => scalar.Value switch
    {
        bool boolean => boolean ? "true" : "false",
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => (string)scalar.Value!
    };

    private static bool IsTemporal(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly) || type == typeof(TimeOnly);

    private static string? ResolvePattern(DataField field)
    {
        var pattern = field.Format;
        if ((pattern is null || pattern.Equals("default", StringComparison.OrdinalIgnoreCase))
            && field.LogicalType is not null)
            DefaultFormats.TryGetMapping(field.LogicalType, out pattern);
        return string.IsNullOrEmpty(pattern) || pattern == "any" ? null : pattern;
    }
}
