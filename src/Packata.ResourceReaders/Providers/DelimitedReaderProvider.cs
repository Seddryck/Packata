using System.Data;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders.Providers;

internal sealed class DelimitedReaderProvider : IDataEndpointIndexedReaderProvider, IReaderBindingProvider
{
    private readonly IReadOnlyList<IDelimitedDialectResolver> _dialectResolvers;
    internal bool DecodeCategories { get; set; }
    public string BindingKey => DelimitedFieldBinding.ReaderKey;

    public Type? GetDefaultRepresentation(DataField field) => DelimitedSpatialFields.GetDefaultRepresentation(field);

    public string? GetUnconvertedText(IDataRecord row, int ordinal) => row.GetString(ordinal);

    public ReaderFieldBinding? TryBind(ReaderRepresentationRequest request)
    {
        var binding = DelimitedSpatialFields.TryBind(request) ?? InternetValueProfiles.TryBind(request);
        if (binding is not null) return new(request.RuntimeType, binding);

        if (request.RuntimeType == typeof(string))
        {
            return new(request.RuntimeType, new DelimitedFieldBinding((schema, field, type) =>
                schema.WithField(type, field.Name, builder => builder.WithDataSourceTypeName(field.LogicalType ?? "string"))));
        }

        var mapper = new RuntimeTypeMapper();
        var native = mapper.Resolve(request.Field, TypeMappingOptions.LegacyCompatibility).RuntimeType;
        if (native is not null && native != typeof(object) && RepresentationType.Same(native, request.RuntimeType))
        {
            return new(native, new DelimitedFieldBinding((schema, field, _) =>
                PocketCsvReaderProviderDefaults.AddField(schema, mapper, field, DecodeCategories)));
        }

        return null;
    }

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

        var open = new ReaderOpenContext(context.Request, context.Format, []) { FieldBindings = context.FieldBindings };
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
        var registrations = context.FieldBindings.Values.Select(DelimitedBinding)
            .Select(binding => binding.ConfigureParsers).OfType<Action<RuntimeParsersDescriptorBuilder>>()
            .Distinct().ToArray();
        if (registrations.Length > 0)
        {
            builder.WithParsers(parsers =>
            {
                foreach (var register in registrations)
                    register(parsers);
                return parsers;
            });
        }
        var schemaBuilder = CreateSchema(context);
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

    private ISchemaDescriptorBuilder? CreateSchema(ReaderOpenContext context)
    {
        if (context.Schema is not { Fields.Count: > 0 } schema) return null;

        var schemaBuilder = new SchemaDescriptorBuilder().Indexed();
        var mapper = new RuntimeTypeMapper();
        for (var ordinal = 0; ordinal < schema.Fields.Count; ordinal++)
        {
            var field = schema.Fields[ordinal];
            if (context.FieldBindings.TryGetValue(ordinal, out var binding))
                DelimitedBinding(binding).BindField(schemaBuilder, field, binding.RuntimeType);
            else
                PocketCsvReaderProviderDefaults.AddField(schemaBuilder, mapper, field, DecodeCategories);
        }
        return schemaBuilder;
    }

    private static DelimitedFieldBinding DelimitedBinding(ReaderFieldBinding binding) =>
        binding.Configuration as DelimitedFieldBinding
        ?? throw new InvalidOperationException($"The delimited binding for '{binding.RuntimeType.FullName}' must supply a {nameof(DelimitedFieldBinding)}.");

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
