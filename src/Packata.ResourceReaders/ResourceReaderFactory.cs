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
    private readonly string _rootPath;

    public ResourceReaderFactory(IEndpointStreamResolver? streams = null, string? rootPath = null)
    {
        _streams = streams ?? new DefaultEndpointStreamResolver();
        _rootPath = rootPath ?? string.Empty;
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

        var format = InferFormat(endpoint, paths);
        var opened = new List<Stream>();
        try
        {
            foreach (var path in paths.Paths)
                opened.Add(await _streams.OpenAsync(path, cancellationToken).ConfigureAwait(false));
            IDataReader reader = format switch
            {
                "xlsx" or "xls" => OpenSpreadsheet(opened, endpoint.Format),
                "parquet" or "pqt" => await ParquetDataReader.CreateAsync(opened).ConfigureAwait(false),
                _ => OpenDelimited(opened, endpoint.Format, schema)
            };
            return new OwnedDataReader(reader, opened.Cast<IDisposable>().ToArray());
        }
        catch
        {
            foreach (var stream in opened) stream.Dispose();
            throw;
        }
    }

    private static string InferFormat(DataEndpoint endpoint, PathLocation paths)
    {
        var explicitFormat = endpoint.Format?.Name?.TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(explicitFormat))
            return explicitFormat.EndsWith(".gz") ? explicitFormat[..^3] : explicitFormat;
        var value = paths.Paths[0];
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) value = uri.AbsolutePath;
        var extension = Path.GetExtension(value).TrimStart('.').ToLowerInvariant();
        return extension == "gz"
            ? Path.GetExtension(Path.GetFileNameWithoutExtension(value)).TrimStart('.').ToLowerInvariant()
            : extension;
    }

    private static IDataReader OpenDelimited(IReadOnlyList<Stream> streams, DataFormat? format, DataSchema? schema)
    {
        var dialect = new DialectDescriptorBuilder();
        if (TryOption(format, "delimiter", out char delimiter)) dialect.WithDelimiter(delimiter);
        if (TryOption(format, "lineTerminator", out string? terminator) && terminator is not null) dialect.WithLineTerminator(terminator);
        if (TryOption(format, "header", out bool header)) dialect.WithHeader(header);
        if (TryOption(format, "quoteChar", out char quote)) dialect.WithQuoteChar(quote);

        ISchemaDescriptorBuilder? schemaBuilder = null;
        if (schema is { Fields.Count: > 0 })
        {
            schemaBuilder = new SchemaDescriptorBuilder().Indexed();
            var mapper = new RuntimeTypeMapper();
            foreach (var field in schema.Fields)
                schemaBuilder.WithField(mapper.Map(field.LogicalType, field.Format), field.Name,
                    builder => field.LogicalType is null ? builder : builder.WithDataSourceTypeName(field.LogicalType));
        }

        var resource = new ResourceDescriptorBuilder();
        if (!string.IsNullOrWhiteSpace(format?.Encoding)) resource.WithEncoding(format.Encoding);
        if (!string.IsNullOrWhiteSpace(format?.Compression)) resource.WithCompression(format.Compression);
        var builder = new CsvReaderBuilder().WithDialect(dialect).WithResource(resource);
        if (schemaBuilder is not null) builder.WithSchema(schemaBuilder);
        var reader = builder.Build();
        return streams.Count == 1
            ? reader.ToDataReader(streams[0])
            : reader.ToDataReader(streams.Select(stream => (Func<Stream>)(() => stream)));
    }

    private static IDataReader OpenSpreadsheet(IReadOnlyList<Stream> streams, DataFormat? format)
    {
        if (streams.Count != 1) throw new InvalidOperationException("Spreadsheet endpoints require exactly one path.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var reader = ExcelReaderFactory.CreateReader(streams[0]);
        var sheetNumber = TryOption(format, "sheetNumber", out int number) ? number : 1;
        var sheetName = TryOption(format, "sheetName", out string? name) ? name : null;
        for (var current = 1; current < sheetNumber || (sheetName is not null && reader.Name != sheetName); current++)
            if (!reader.NextResult()) throw new InvalidOperationException("The configured spreadsheet sheet was not found.");
        var headers = new List<string>();
        var hasHeader = !TryOption(format, "header", out bool header) || header;
        if (hasHeader && reader.Read())
            for (var index = 0; index < reader.FieldCount; index++) headers.Add(reader.GetValue(index)?.ToString() ?? string.Empty);
        return new Packata.ResourceReaders.Tabular.ExcelDataReader(reader, [.. headers]);
    }

    private IDataReader OpenDatabase(DataEndpoint endpoint, ConnectionLocation location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = location.ConnectionUrl ?? throw new ArgumentException("ConnectionUrl is required.", nameof(endpoint));
        new ProviderFactoriesRegistrator().Register();
        var factory = new ConnectionUrlFactory(new SchemeRegistryBuilder().WithRootPath(_rootPath)
            .WithAssemblies(typeof(SchemeRegistryBuilder).Assembly).WithAutoDiscoveredMappings().Build());
        var connectionUrl = factory.Instantiate(url);
        var connection = connectionUrl.Open();
        try
        {
            var command = connection.CreateCommand();
            var table = OptionString(endpoint.Format, "table")
                ?? throw new ArgumentException("Database endpoint format requires a table option.", nameof(endpoint));
            var ns = location.Namespace ?? OptionString(endpoint.Format, "namespace");
            command.CommandText = string.IsNullOrEmpty(ns)
                ? $"SELECT * FROM {connectionUrl.Dialect.Renderer.Render(table, "identity")}" :
                  $"SELECT * FROM {connectionUrl.Dialect.Renderer.Render(ns, "identity")}.{connectionUrl.Dialect.Renderer.Render(table, "identity")}";
            var reader = command.ExecuteReader();
            return new OwnedDataReader(reader, command, connection);
        }
        catch { connection.Dispose(); throw; }
    }

    private static string? OptionString(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;

    private static bool TryOption<T>(DataFormat? format, string name, out T value)
    {
        if (format?.Options.TryGetValue(name, out var raw) == true && raw is T typed)
        { value = typed; return true; }
        value = default!; return false;
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
