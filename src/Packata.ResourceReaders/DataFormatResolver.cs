using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class DataFormatResolver
{
    public static ResolvedDataFormat Resolve(DataEndpoint endpoint)
    {
        var explicitFormat = Normalize(endpoint.Format?.Name);
        var compression = Normalize(endpoint.Format?.Compression);
        if (explicitFormat?.EndsWith(".gz", StringComparison.Ordinal) == true)
        {
            explicitFormat = explicitFormat[..^3];
            compression ??= "gzip";
        }

        var pathFormats = endpoint.Location is PathLocation paths
            ? paths.Paths.Select(PathFormat).Where(value => value is not null).Distinct().ToArray()
            : [];
        if (pathFormats.Length > 1)
            throw new InvalidOperationException("All paths in an endpoint must use the same format.");

        var mediaType = endpoint.Format?.MediaType?.Split(';', '+')[0].Trim().ToLowerInvariant();
        var name = explicitFormat ?? MediaTypeFormat(mediaType) ?? pathFormats.SingleOrDefault()?.Name ?? string.Empty;
        compression ??= MediaTypeCompression(mediaType) ?? pathFormats.SingleOrDefault()?.Compression;
        return new ResolvedDataFormat(name, mediaType, compression);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimStart('.').ToLowerInvariant();

    private static PathDataFormat? PathFormat(string path)
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
        return extension is null ? null : new PathDataFormat(extension, compression);
    }

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
