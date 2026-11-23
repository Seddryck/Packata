using System.Net;
using System.Net.Sockets;
using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal static class InternetValueProfiles
{
    internal static ReaderRepresentationProfile UriValues { get; } = new(
        "UriValues",
        field => GetStringFormat(field) is "uri" or "uri-reference" ? typeof(Uri) : null,
        new(null, BindUri));

    internal static ReaderRepresentationProfile NetworkValues { get; } = new(
        "NetworkValues",
        field => GetStringFormat(field) is "ipv4" or "ipv6" ? typeof(IPAddress) : null,
        new(null, BindAddress));

    private static string? GetStringFormat(DataField field)
        => string.Equals(field.LogicalType?.Trim(), "string", StringComparison.OrdinalIgnoreCase)
            ? field.Format?.Trim().ToLowerInvariant()
            : null;

    private static void BindUri(ISchemaDescriptorBuilder schema, DataField field, Type type)
    {
        var kind = GetStringFormat(field) == "uri" ? UriKind.Absolute : UriKind.RelativeOrAbsolute;
        schema.WithField(type, field.Name, builder => builder.WithDataSourceTypeName("string")
            .WithParser<Uri>(text => ParseUri(text, kind)));
    }

    private static Uri ParseUri(ReadOnlySpan<char> text, UriKind kind)
    {
        if (Uri.TryCreate(text.ToString(), kind, out var uri) && uri.IsWellFormedOriginalString())
            return uri;

        throw new FormatException(kind == UriKind.Absolute
            ? "Expected an absolute URI."
            : "Expected a URI reference.");
    }

    private static void BindAddress(ISchemaDescriptorBuilder schema, DataField field, Type type)
    {
        var ipv4 = GetStringFormat(field) == "ipv4";
        schema.WithField(type, field.Name, builder => builder.WithDataSourceTypeName("string")
            .WithParser<IPAddress>(text => ParseAddress(text, ipv4)));
    }

    private static IPAddress ParseAddress(ReadOnlySpan<char> text, bool ipv4)
    {
        var family = ipv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
        var validSyntax = ipv4 ? IsDottedDecimal(text) : text.IndexOfAny('[', ']', '%') < 0;
        if (validSyntax && IPAddress.TryParse(text, out var address) && address.AddressFamily == family)
            return address;

        throw new FormatException(ipv4 ? "Expected an IPv4 address in dotted-decimal notation." : "Expected an IPv6 address.");
    }

    private static bool IsDottedDecimal(ReadOnlySpan<char> text)
    {
        Span<Range> parts = stackalloc Range[4];
        if (text.Split(parts, '.') != parts.Length) return false;

        foreach (var part in parts)
        {
            var octet = text[part];
            if (octet.IsEmpty || octet.Length > 3 || (octet.Length > 1 && octet[0] == '0')
                || octet.IndexOfAnyExceptInRange('0', '9') >= 0)
                return false;
        }

        return true;
    }
}
