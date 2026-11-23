using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders.Providers;

internal sealed class DelimitedReaderProvider : IDataEndpointIndexedReaderProvider
{
    private readonly IReadOnlyList<IDelimitedDialectResolver> _dialectResolvers;
    internal bool DecodeCategories { get; set; }
    internal IReadOnlyList<ReaderRepresentationProfile> RepresentationProfiles { get; set; } = [];

    public DelimitedReaderProvider(IEnumerable<IDelimitedDialectResolver>? dialectResolvers = null)
    {
        _dialectResolvers = [.. dialectResolvers ?? [], new FormatOptionsDialectResolver()];
    }

    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation &&
        (format.Name is "csv" or "tsv" or "psv" ||
         (string.IsNullOrEmpty(format.Name) && HasDelimiterOption(request.Endpoint.Format)));

    public async ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialect = await ResolveDialectAsync(context, cancellationToken).ConfigureAwait(false);
        var reader = CreateBuilder(context, dialect).Build();
        IDataReader dataReader = context.Streams.Count == 1
            ? reader.ToDataReader(context.Streams[0])
            : reader.ToDataReader(context.Streams.Select(stream => (Func<Stream>)(() => stream)));
        return dataReader;
    }

    public async ValueTask<IDataReader> OpenIndexedAsync(IndexedReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.StreamResolver is not DefaultEndpointStreamResolver || DecodeCategories)
            throw new NotSupportedException("Delimited indexed lookups require the local filesystem reader with category decoding disabled.");
        if (context.Equalities.GroupBy(equality => equality.Index.Kind).Any(group => group.Count() > 1))
            throw new NotSupportedException("Delimited indexed lookups support at most one equality of each index kind per file.");

        var open = new ReaderOpenContext(context.Request, context.Format, []);
        var dialect = await ResolveDialectAsync(open, cancellationToken).ConfigureAwait(false);
        var builder = CreateBuilder(open, dialect);
        var location = (PathLocation)context.Request.Endpoint.Location;
        var paths = location.Paths.Select(LocalPath).ToArray();
        var query = builder.FromFiles(paths).WithPartitions(partitions => partitions.FromPath(path => path)).Build();
        var hasHeader = dialect.Build().Header;
        foreach (var equality in context.Equalities)
            query = DelimitedIndexLookup.Apply(query, equality, hasHeader);

        return query.ToDataReader();
    }

    private CsvReaderBuilder CreateBuilder(ReaderOpenContext context, DialectDescriptorBuilder dialect)
    {
        var resource = CreateResource(context.Endpoint.Format, context.Format.Compression);
        var builder = new CsvReaderBuilder().WithDialect(dialect).WithResource(resource);
        if (RepresentationProfiles.Count > 0)
        {
            builder.WithParsers(parsers =>
            {
                foreach (var profile in RepresentationProfiles)
                    profile.Delimited.ConfigureParsers?.Invoke(parsers);
                return parsers;
            });
        }
        var schemaBuilder = CreateSchema(context.Schema);
        if (schemaBuilder is not null) builder.WithSchema(schemaBuilder);
        return builder;
    }

    private static string LocalPath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return path;
        if (!uri.IsFile)
            throw new NotSupportedException("Delimited indexed lookups require local filesystem paths.");
        return uri.LocalPath;
    }

    private async ValueTask<DialectDescriptorBuilder> ResolveDialectAsync(ReaderOpenContext context,
        CancellationToken cancellationToken)
    {
        var resolverContext = new DelimitedDialectContext(context.Request, context.Format);
        foreach (var resolver in _dialectResolvers)
        {
            var resolved = await resolver.ResolveAsync(resolverContext, cancellationToken).ConfigureAwait(false);
            if (resolved is not null) return BuildDialect(context.Format.Name, resolved);
        }

        return BuildDialect(context.Format.Name, null);
    }

    private static DialectDescriptorBuilder BuildDialect(string? formatName, DelimitedDialect? resolved)
    {
        var defaultDelimiter = formatName switch { "tsv" => '\t', "psv" => '|', _ => ',' };
        var dialect = new DialectDescriptorBuilder();
        dialect.WithDelimiter(resolved?.Delimiter ?? defaultDelimiter);
        if (resolved?.LineTerminator is not null) dialect.WithLineTerminator(resolved.LineTerminator);
        if (resolved?.Header is not null) dialect.WithHeader(resolved.Header.Value);
        if (resolved?.QuoteChar is not null) dialect.WithQuoteChar(resolved.QuoteChar.Value);
        return dialect;
    }

    private static char? CharacterOption(DataFormat? format, string name)
    {
        if (format?.Options.TryGetValue(name, out var raw) != true || raw is null)
            return null;

        if (raw is char character)
            return character;

        if (raw is string { Length: 1 } text)
            return text[0];

        throw new ArgumentException($"Format option '{name}' must contain exactly one character.", nameof(format));
    }

    private ISchemaDescriptorBuilder? CreateSchema(DataSchema? schema)
    {
        if (schema is not { Fields.Count: > 0 }) return null;

        var schemaBuilder = new SchemaDescriptorBuilder().Indexed();
        var mapper = new RuntimeTypeMapper();
        foreach (var field in schema.Fields)
        {
            if (ReaderRepresentationProfile.Resolve(RepresentationProfiles, field) is { } representation)
                representation.Profile.Delimited.BindField(schemaBuilder, field, representation.Type);
            else if (!DelimitedSpatialFields.TryAdd(schemaBuilder, field))
                PocketCsvReaderProviderDefaults.AddField(schemaBuilder, mapper, field, DecodeCategories);
        }
        return schemaBuilder;
    }

    private static ResourceDescriptorBuilder CreateResource(DataFormat? format, string? compression)
    {
        var resource = new ResourceDescriptorBuilder();
        if (!string.IsNullOrWhiteSpace(format?.Encoding)) resource.WithEncoding(format.Encoding);
        if (!string.IsNullOrWhiteSpace(compression)) resource.WithCompression(compression);
        return resource;
    }

    private static bool HasDelimiterOption(DataFormat? format) =>
        format?.Options.ContainsKey("delimiter") == true;

    private sealed class FormatOptionsDialectResolver : IDelimitedDialectResolver
    {
        public ValueTask<DelimitedDialect?> ResolveAsync(DelimitedDialectContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = context.Request.Endpoint.Format;
            var delimiter = CharacterOption(options, "delimiter");
            var quote = CharacterOption(options, "quoteChar");
            var terminator = options?.Options.TryGetValue("lineTerminator", out var rawTerminator) == true
                ? rawTerminator as string : null;
            bool? header = options?.Options.TryGetValue("header", out var rawHeader) == true && rawHeader is bool value
                ? value : null;
            return ValueTask.FromResult<DelimitedDialect?>(
                delimiter is null && quote is null && terminator is null && header is null
                    ? null
                    : new DelimitedDialect(delimiter, terminator, header, quote));
        }
    }
}
