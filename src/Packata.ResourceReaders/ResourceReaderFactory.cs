using System.Data;
using System.Text;
using DubUrl;
using DubUrl.Mapping;
using DubUrl.Registering;
using ExcelDataReader;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Tabular;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

/// <summary>
/// Concurrent-safe reader entry point for canonical endpoints. A returned reader owns the streams,
/// commands, and connections opened for it and releases them when disposed.
/// </summary>
public sealed class ResourceReaderFactory : IDataEndpointReaderFactory
{
    private readonly IEndpointStreamResolver _streams;
    private readonly IDatabaseSessionFactory _databases;

    public ResourceReaderFactory(IEndpointStreamResolver? streams = null, string? rootPath = null)
        : this(streams, new DubUrlDatabaseSessionFactory(rootPath ?? string.Empty))
    { }

    internal ResourceReaderFactory(IEndpointStreamResolver? streams, IDatabaseSessionFactory databases)
    {
        _streams = streams ?? new DefaultEndpointStreamResolver();
        _databases = databases;
    }

    public async ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Location is ConnectionLocation connection)
            return OpenDatabase(endpoint, connection, cancellationToken);
        if (endpoint.Location is not PathLocation paths || paths.Paths.Count == 0)
            throw new NotSupportedException($"Endpoint '{endpoint.Id}' does not expose readable paths or a connection.");

        var format = ResolveFormat(endpoint, paths);
        var opened = new List<Stream>();
        try
        {
            foreach (var path in paths.Paths)
                opened.Add(await _streams.OpenAsync(path, cancellationToken).ConfigureAwait(false));
            IDataReader reader = format.Name switch
            {
                "xlsx" or "xls" => OpenSpreadsheet(opened, endpoint.Format),
                "parquet" or "pqt" => await ParquetDataReader.CreateAsync(opened).ConfigureAwait(false),
                _ => OpenDelimited(opened, endpoint.Format, schema, format)
            };
            return new OwnedDataReader(reader, opened.Cast<IDisposable>().ToArray());
        }
        catch
        {
            foreach (var stream in opened) stream.Dispose();
            throw;
        }
    }

    private static ResolvedFormat ResolveFormat(DataEndpoint endpoint, PathLocation paths)
    {
        var explicitFormat = Normalize(endpoint.Format?.Name);
        var compression = Normalize(endpoint.Format?.Compression);
        if (explicitFormat?.EndsWith(".gz", StringComparison.Ordinal) == true)
        {
            explicitFormat = explicitFormat[..^3];
            compression ??= "gzip";
        }

        var pathFormats = paths.Paths.Select(PathFormat).Where(value => value is not null).Distinct().ToArray();
        if (pathFormats.Length > 1)
            throw new InvalidOperationException("All paths in an endpoint must use the same format.");

        var mediaType = endpoint.Format?.MediaType?.Split(';', '+')[0].Trim().ToLowerInvariant();
        var name = explicitFormat ?? MediaTypeFormat(mediaType) ?? pathFormats.SingleOrDefault()?.Name ?? string.Empty;
        compression ??= MediaTypeCompression(mediaType) ?? pathFormats.SingleOrDefault()?.Compression;
        var delimiter = name switch { "tsv" => '\t', "psv" => '|', _ => ',' };
        return new ResolvedFormat(name, compression, delimiter);
    }

    private static IDataReader OpenDelimited(IReadOnlyList<Stream> streams, DataFormat? format, DataSchema? schema,
        ResolvedFormat resolved)
    {
        var dialect = CreateDialect(format, resolved.Delimiter);
        var resource = CreateResource(format, resolved.Compression);
        var builder = new CsvReaderBuilder().WithDialect(dialect).WithResource(resource);
        var schemaBuilder = CreateSchema(schema);
        if (schemaBuilder is not null) builder.WithSchema(schemaBuilder);
        var reader = builder.Build();
        return streams.Count == 1
            ? reader.ToDataReader(streams[0])
            : reader.ToDataReader(streams.Select(stream => (Func<Stream>)(() => stream)));
    }

    private static DialectDescriptorBuilder CreateDialect(DataFormat? format, char defaultDelimiter)
    {
        var dialect = new DialectDescriptorBuilder();
        dialect.WithDelimiter(defaultDelimiter);
        if (TryOption(format, "delimiter", out char delimiter)) dialect.WithDelimiter(delimiter);
        if (TryOption(format, "lineTerminator", out string? terminator) && terminator is not null) dialect.WithLineTerminator(terminator);
        if (TryOption(format, "header", out bool header)) dialect.WithHeader(header);
        if (TryOption(format, "quoteChar", out char quote)) dialect.WithQuoteChar(quote);
        return dialect;
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimStart('.').ToLowerInvariant();

    private static ResolvedFormat? PathFormat(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;
        var extension = Normalize(Path.GetExtension(path));
        if (extension is null) return null;
        string? compression = null;
        if (extension is "gz" or "gzip")
        {
            compression = "gzip";
            extension = Normalize(Path.GetExtension(Path.GetFileNameWithoutExtension(path)));
        }
        return extension is null ? null : new ResolvedFormat(extension, compression, ',');
    }

    private static string? MediaTypeFormat(string? mediaType) => mediaType switch
    {
        "text/csv" => "csv",
        "text/tsv" or "text/tab-separated-values" => "tsv",
        "text/psv" => "psv",
        "application/vnd.ms-excel" => "xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
        "application/vnd.apache.parquet" => "parquet",
        _ => null
    };

    private static string? MediaTypeCompression(string? mediaType) => mediaType switch
    {
        "application/gzip" => "gzip",
        "application/x-deflate" => "deflate",
        "application/zip" => "zip",
        _ => null
    };

    private static IDataReader OpenSpreadsheet(IReadOnlyList<Stream> streams, DataFormat? format)
    {
        if (streams.Count != 1) throw new InvalidOperationException("Spreadsheet endpoints require exactly one path.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var reader = ExcelReaderFactory.CreateReader(streams[0]);
        var (sheetNumber, sheetName) = ResolveSheet(format);
        MoveToSheet(reader, sheetNumber, sheetName);
        return new Packata.ResourceReaders.Tabular.ExcelDataReader(reader, ReadHeaders(reader, format));
    }

    private static (int Number, string? Name) ResolveSheet(DataFormat? format)
    {
        var sheetNumber = TryOption(format, "sheetNumber", out int number) ? number : 1;
        var sheetName = TryOption(format, "sheetName", out string? name) ? name : null;
        if (sheetName is not null && TryOption<int>(format, "sheetNumber", out _))
            throw new ArgumentException("Specify either sheetName or sheetNumber, not both.", nameof(format));
        return (sheetNumber, sheetName);
    }

    private static void MoveToSheet(IExcelDataReader reader, int sheetNumber, string? sheetName)
    {
        for (var current = 1; current < sheetNumber || (sheetName is not null && reader.Name != sheetName); current++)
            if (!reader.NextResult()) throw new InvalidOperationException("The configured spreadsheet sheet was not found.");
    }

    private static string[] ReadHeaders(IExcelDataReader reader, DataFormat? format)
    {
        var headers = new List<string>();
        var hasHeader = !TryOption(format, "header", out bool header) || header;
        if (hasHeader && reader.Read())
            for (var index = 0; index < reader.FieldCount; index++) headers.Add(reader.GetValue(index)?.ToString() ?? string.Empty);
        return [.. headers];
    }

    private IDataReader OpenDatabase(DataEndpoint endpoint, ConnectionLocation location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = location.ConnectionUrl ?? throw new ArgumentException("ConnectionUrl is required.", nameof(endpoint));
        var session = _databases.Open(url);
        var connection = session.Connection;
        IDbCommand? command = null;
        try
        {
            command = connection.CreateCommand();
            var table = OptionString(endpoint.Format, "table")
                ?? throw new ArgumentException("Database endpoint format requires a table option.", nameof(endpoint));
            var ns = location.Namespace ?? OptionString(endpoint.Format, "namespace");
            command.CommandText = string.IsNullOrEmpty(ns)
                ? $"SELECT * FROM {session.RenderIdentifier(table)}" :
                  $"SELECT * FROM {session.RenderIdentifier(ns)}.{session.RenderIdentifier(table)}";
            var reader = command.ExecuteReader();
            return new OwnedDataReader(reader, command, connection);
        }
        catch { command?.Dispose(); connection.Dispose(); throw; }
    }

    private sealed record ResolvedFormat(string Name, string? Compression, char Delimiter);

    private static string? OptionString(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;

    private static bool TryOption<T>(DataFormat? format, string name, out T value)
    {
        if (format?.Options.TryGetValue(name, out var raw) == true && raw is T typed)
        { value = typed; return true; }
        value = default!; return false;
    }
}

internal interface IDatabaseSessionFactory
{
    DatabaseSession Open(string connectionUrl);
}

internal sealed record DatabaseSession(IDbConnection Connection, Func<string, string> RenderIdentifier);

internal sealed class DubUrlDatabaseSessionFactory(string rootPath) : IDatabaseSessionFactory
{
    public DatabaseSession Open(string url)
    {
        new ProviderFactoriesRegistrator().Register();
        var factory = new ConnectionUrlFactory(new SchemeRegistryBuilder().WithRootPath(rootPath)
            .WithAssemblies(typeof(SchemeRegistryBuilder).Assembly).WithAutoDiscoveredMappings().Build());
        var connectionUrl = factory.Instantiate(url);
        return new DatabaseSession(connectionUrl.Open(),
            value => connectionUrl.Dialect.Renderer.Render(value, "identity"));
    }
}

public sealed class DefaultEndpointStreamResolver : IEndpointStreamResolver
{
    private static readonly HttpClient Http = new();
    public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return await Http.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    }
}

internal sealed class OwnedDataReader(IDataReader inner, params IDisposable[] owned) : IDataReader
{
    public object this[int i] => inner[i]; public object this[string name] => inner[name];
    public int Depth => inner.Depth; public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected; public int FieldCount => inner.FieldCount;
    public void Close() => Dispose();
    public void Dispose() { inner.Dispose(); foreach (var value in owned) value.Dispose(); }
    public bool GetBoolean(int i) => inner.GetBoolean(i); public byte GetByte(int i) => inner.GetByte(i);
    public long GetBytes(int i, long o, byte[]? b, int bo, int l) => inner.GetBytes(i, o, b, bo, l);
    public char GetChar(int i) => inner.GetChar(i);
    public long GetChars(int i, long o, char[]? b, int bo, int l) => inner.GetChars(i, o, b, bo, l);
    public IDataReader GetData(int i) => inner.GetData(i); public string GetDataTypeName(int i) => inner.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => inner.GetDateTime(i); public decimal GetDecimal(int i) => inner.GetDecimal(i);
    public double GetDouble(int i) => inner.GetDouble(i); public Type GetFieldType(int i) => inner.GetFieldType(i);
    public float GetFloat(int i) => inner.GetFloat(i); public Guid GetGuid(int i) => inner.GetGuid(i);
    public short GetInt16(int i) => inner.GetInt16(i); public int GetInt32(int i) => inner.GetInt32(i);
    public long GetInt64(int i) => inner.GetInt64(i); public string GetName(int i) => inner.GetName(i);
    public int GetOrdinal(string name) => inner.GetOrdinal(name); public DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public string GetString(int i) => inner.GetString(i); public object GetValue(int i) => inner.GetValue(i);
    public int GetValues(object[] values) => inner.GetValues(values); public bool IsDBNull(int i) => inner.IsDBNull(i);
    public bool NextResult() => inner.NextResult(); public bool Read() => inner.Read();
}
