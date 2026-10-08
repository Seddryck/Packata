using System.Collections;
using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;

namespace Packata.ResourceReaders.FixedWidth.Providers;

internal sealed class FixedWidthReaderProvider : IDataEndpointReaderProvider
{
    public bool CanOpen(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "fixed-width" or "fixedwidth" or "fwf";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schema = context.Schema is { Fields.Count: > 0 }
            ? context.Schema
            : throw new ArgumentException("A fixed-width reader requires a canonical schema.", nameof(context));
        var widths = IntegerList(context.Endpoint.Format, "widths")
            ?? throw new ArgumentException("Fixed-width format option 'widths' is required.", nameof(context));
        var offsets = IntegerList(context.Endpoint.Format, "offsets") ?? CumulativeOffsets(widths);
        ValidateLayout(schema, offsets, widths, IntegerOption(context.Endpoint.Format, "recordWidth"));

        var builder = new FixedWidthReaderBuilder()
            .WithLineTerminator(StringOption(context.Endpoint.Format, "lineTerminator") ?? "\n")
            .AllowTrailingCharacters(BooleanOption(context.Endpoint.Format, "allowTrailingCharacters") ?? false)
            .WithHeader(BooleanOption(context.Endpoint.Format, "header") ?? false)
            .WithResource(Resource(context))
            .WithSchema(Schema(schema));
        for (var i = 0; i < schema.Fields.Count; i++)
            builder.WithField(schema.Fields[i].Name, offsets[i], widths[i], FixedWidthPadding.None, ' ');
        var reader = builder.Build();
        IDataReader dataReader = context.Streams.Count == 1
            ? reader.ToDataReader(context.Streams[0])
            : new SequentialDataReader(context.Streams.Select(reader.ToDataReader));
        return ValueTask.FromResult(dataReader);
    }

    private static ISchemaDescriptorBuilder Schema(DataSchema schema)
    {
        var builder = new SchemaDescriptorBuilder().Indexed();
        var mapper = new RuntimeTypeMapper();
        foreach (var field in schema.Fields)
            builder.WithField(mapper.Map(field.LogicalType, field.Format), field.Name,
                value => field.LogicalType is null ? value : value.WithDataSourceTypeName(field.LogicalType));
        return builder;
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

    private static void ValidateLayout(DataSchema schema, IReadOnlyList<int> offsets,
        IReadOnlyList<int> widths, int? recordWidth)
    {
        if (offsets.Count != schema.Fields.Count || widths.Count != schema.Fields.Count)
            throw new ArgumentException("Fixed-width offsets and widths must match the canonical schema field count.");
        if (offsets.Any(offset => offset < 0))
            throw new ArgumentOutOfRangeException(nameof(offsets), "Fixed-width offsets cannot be negative.");
        if (widths.Any(width => width <= 0))
            throw new ArgumentOutOfRangeException(nameof(widths), "Fixed-width widths must be greater than zero.");
        var ranges = offsets.Zip(widths, (offset, width) => (Start: offset, End: checked(offset + width)))
            .OrderBy(range => range.Start).ToArray();
        if (ranges.Zip(ranges.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
            throw new ArgumentException("Fixed-width fields cannot overlap.");
        if (recordWidth is < 1)
            throw new ArgumentOutOfRangeException(nameof(recordWidth), "Fixed-width recordWidth must be positive.");
        if (recordWidth is not null && ranges.Any(range => range.End > recordWidth))
            throw new ArgumentException("A fixed-width field extends beyond recordWidth.");
    }

    private static int[] CumulativeOffsets(IReadOnlyList<int> widths)
    {
        var offsets = new int[widths.Count];
        for (var i = 1; i < offsets.Length; i++) offsets[i] = checked(offsets[i - 1] + widths[i - 1]);
        return offsets;
    }

    private static int[]? IntegerList(DataFormat? format, string name)
    {
        if (format?.Options.TryGetValue(name, out var raw) != true || raw is null) return null;
        if (raw is IEnumerable<int> integers) return integers.ToArray();
        if (raw is IEnumerable values)
            return values.Cast<object?>().Select(value => Convert.ToInt32(value)).ToArray();
        throw new ArgumentException($"Fixed-width format option '{name}' must be a list of integers.");
    }

    private static int? IntegerOption(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true && value is not null
            ? Convert.ToInt32(value) : null;
    private static bool? BooleanOption(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true && value is not null
            ? Convert.ToBoolean(value) : null;
    private static string? StringOption(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;
}
