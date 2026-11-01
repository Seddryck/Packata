using System.Net;
using System.Web;
using Packata.Core.Contracts;

namespace Packata.Database;

/// <summary>Deterministic URL adaptation; never includes inputs in diagnostic messages.</summary>
public static class DatabaseEndpointResolver
{
    public static string BuildConnectionUrl(ConnectionLocation location, DatabaseRuntimeOptions? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        var url = string.IsNullOrWhiteSpace(location.ConnectionUrl) ? BuildStructuredUrl(location) : location.ConnectionUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !url.Contains("://", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("A valid database connection URL without a fragment is required.", nameof(location));
        ValidateEncoding(url);
        if (runtime is not null && (runtime.Username is null) != (runtime.Password is null))
            throw new ArgumentException("Runtime credentials require both username and password.", nameof(runtime));

        var options = ParseOptions(uri);
        if (runtime is null) return url;
        ApplyRuntimeOptions(options, runtime);
        return RenderUrl(uri, runtime, options);
    }

    private static string BuildStructuredUrl(ConnectionLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.Scheme) || !Uri.CheckSchemeName(location.Scheme))
            throw new ArgumentException("A valid database connection scheme is required.", nameof(location));
        if (string.IsNullOrWhiteSpace(location.Host) && string.IsNullOrWhiteSpace(location.Database))
            throw new ArgumentException("A database connection requires a host or database.", nameof(location));
        if (location.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(location), "The database connection port is invalid.");
        var host = NormalizeHost(location.Host);
        var port = location.Port is null ? "" : $":{location.Port}";
        var database = string.IsNullOrEmpty(location.Database) ? "" : $"/{Uri.EscapeDataString(location.Database)}";
        return $"{location.Scheme}://{host}{port}{database}";
    }

    private static string NormalizeHost(string? value)
    {
        var host = string.IsNullOrWhiteSpace(value) ? "." : value.Trim();
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return $"[{address}]";
        if (host != "." && Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("A valid database host is required.", "location");
        return host;
    }

    private static Dictionary<string, string> ParseOptions(Uri uri)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (!options.TryAdd(HttpUtility.UrlDecode(pair[0]), pair.Length == 2 ? HttpUtility.UrlDecode(pair[1]) : ""))
                throw new ArgumentException("The connection URL contains duplicate provider options.", "location");
        }
        return options;
    }

    private static void ApplyRuntimeOptions(Dictionary<string, string> options, DatabaseRuntimeOptions runtime)
    {
        var runtimeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in runtime.Options ?? new Dictionary<string, string>())
        {
            if (!runtimeKeys.Add(pair.Key))
                throw new ArgumentException("Runtime configuration contains duplicate provider options.", nameof(runtime));
            options[pair.Key] = pair.Value;
        }
    }

    private static string RenderUrl(Uri uri, DatabaseRuntimeOptions runtime, Dictionary<string, string> options)
    {
        var userInfo = runtime.Username is null ? uri.UserInfo :
            $"{Uri.EscapeDataString(runtime.Username)}:{Uri.EscapeDataString(runtime.Password!)}";
        var authority = string.IsNullOrEmpty(userInfo) ? "" : userInfo + "@";
        // Keep URI path segmentation for explicit URLs; structured database names are a single encoded segment.
        var query = options.Count == 0 ? "" : "?" + string.Join('&', options.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return $"{uri.Scheme}://{authority}{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{(uri.AbsolutePath == "/" ? "" : uri.AbsolutePath)}{query}";
    }

    private static void ValidateEncoding(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])))
                throw new ArgumentException("The connection URL contains invalid percent encoding.");
        }
    }
}
