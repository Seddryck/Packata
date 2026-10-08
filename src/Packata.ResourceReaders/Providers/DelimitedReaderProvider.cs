using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders.Providers;

internal sealed class DelimitedReaderProvider : IDataEndpointReaderProvider
{
    public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation;

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        var delimiter = context.Format.Name switch { "tsv" => '\t', "psv" => '|', _ => ',' };
        var dialect = CreateDialect(context.Endpoint.Format, delimiter);
        var resource = CreateResource(context.Endpoint.Format, context.Format.Compression);
        var builder = new CsvReaderBuilder().WithDialect(dialect).WithResource(resource);
        var schemaBuilder = CreateSchema(context.Schema);
        if (schemaBuilder is not null) builder.WithSchema(schemaBuilder);
        var reader = builder.Build();
        IDataReader dataReader = context.Streams.Count == 1
            ? reader.ToDataReader(context.Streams[0])
            : reader.ToDataReader(context.Streams.Select(stream => (Func<Stream>)(() => stream)));
        return ValueTask.FromResult(dataReader);
    }

    private static DialectDescriptorBuilder CreateDialect(DataFormat? format, char defaultDelimiter)
    {
        var dialect = new DialectDescriptorBuilder();
        dialect.WithDelimiter(defaultDelimiter);
        if (TryCharacterOption(format, "delimiter", out var delimiter)) dialect.WithDelimiter(delimiter);
        if (TryOption(format, "lineTerminator", out string? terminator) && terminator is not null)
            dialect.WithLineTerminator(terminator);
        if (TryOption(format, "header", out bool header)) dialect.WithHeader(header);
        if (TryCharacterOption(format, "quoteChar", out var quote)) dialect.WithQuoteChar(quote);
        return dialect;
    }

    private static bool TryCharacterOption(DataFormat? format, string name, out char value)
    {
        if (format?.Options.TryGetValue(name, out var raw) != true || raw is null)
        {
            value = default;
            return false;
        }

        if (raw is char character)
        {
            value = character;
            return true;
        }

        if (raw is string { Length: 1 } text)
        {
            value = text[0];
            return true;
        }

        throw new ArgumentException($"Format option '{name}' must contain exactly one character.", nameof(format));
    }

    private static ISchemaDescriptorBuilder? CreateSchema(DataSchema? schema)
    {
        if (schema is not { Fields.Count: > 0 }) return null;

        var schemaBuilder = new SchemaDescriptorBuilder().Indexed();
        var mapper = new RuntimeTypeMapper();
        foreach (var field in schema.Fields)
            schemaBuilder.WithField(mapper.Map(field.LogicalType, field.Format), field.Name,
                builder => field.LogicalType is null ? builder : builder.WithDataSourceTypeName(field.LogicalType));
        return schemaBuilder;
    }

    private static ResourceDescriptorBuilder CreateResource(DataFormat? format, string? compression)
    {
        var resource = new ResourceDescriptorBuilder();
        if (!string.IsNullOrWhiteSpace(format?.Encoding)) resource.WithEncoding(format.Encoding);
        if (!string.IsNullOrWhiteSpace(compression)) resource.WithCompression(compression);
        return resource;
    }

    private static bool TryOption<T>(DataFormat? format, string name, out T value)
    {
        if (format?.Options.TryGetValue(name, out var raw) == true && raw is T typed)
        { value = typed; return true; }
        value = default!;
        return false;
    }
}
