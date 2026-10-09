using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed class DataFormatResolver
{
    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyDictionary<string, string> _extensions;

    public DataFormatResolver(DataFormatResolutionOptions? options = null)
    {
        options ??= new DataFormatResolutionOptions();
        _aliases = options.Aliases;
        _extensions = options.Extensions;
    }

    public ResolvedDataFormat Resolve(DataEndpoint endpoint)
    {
        var explicitFormat = Normalize(endpoint.Format?.Name);
        var compression = Normalize(endpoint.Format?.Compression);
        if (explicitFormat?.EndsWith(".gz", StringComparison.Ordinal) == true)
        {
            explicitFormat = explicitFormat[..^3];
            compression ??= "gzip";
        }

        explicitFormat = Canonicalize(explicitFormat);

        var pathFormats = endpoint.Location is PathLocation paths
            ? paths.Paths.Select(PathFormat).Where(value => value is not null).Distinct().ToArray()
            : [];
        if (pathFormats.Length > 1)
            throw new InvalidOperationException("All paths in an endpoint must use the same format.");

        var mediaType = endpoint.Format?.MediaType?.Split(';', '+')[0].Trim().ToLowerInvariant();
        var name = explicitFormat ?? Canonicalize(MediaTypeFormat(mediaType))
            ?? pathFormats.SingleOrDefault()?.Name ?? string.Empty;
        compression ??= MediaTypeCompression(mediaType) ?? pathFormats.SingleOrDefault()?.Compression;
        return new ResolvedDataFormat(name, mediaType, compression);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimStart('.').ToLowerInvariant();

    private PathDataFormat? PathFormat(string path)
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
        return extension is null ? null : new PathDataFormat(
            _extensions.GetValueOrDefault(extension) ?? extension,
            compression);
    }

    private string? Canonicalize(string? value) =>
        value is not null && _aliases.TryGetValue(value, out var canonical) ? canonical : value;

    private static string? MediaTypeFormat(string? mediaType) => mediaType switch
    {
        "text/csv" => "csv",
        "text/tsv" or "text/tab-separated-values" => "tsv",
        "text/psv" => "psv",
        "application/x-ndjson" or "application/ndjson" => "ndjson",
        "text/x-fixed-width" => "fixed-width",
        "text/x-ltsv" or "text/ltsv" => "ltsv",
        "application/logfmt" or "text/x-logfmt" => "logfmt",
        "text/x-common-log" => "common-log",
        "text/x-w3c-log" => "w3c-log",
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

    private sealed record PathDataFormat(string Name, string? Compression);
}
