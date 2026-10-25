namespace Packata.DataPackage;

internal static class PackageReferenceSyntax
{
    public static bool IsRegistryName(string value)
    {
        if (value.Length == 0 || !IsAlphaNumeric(value[0])) return false;
        return value.All(character => IsAlphaNumeric(character) || character is '.' or '_' or '-');
    }

    private static bool IsAlphaNumeric(char character) => character is >= 'a' and <= 'z' or >= '0' and <= '9';

    public static bool IsDescriptorUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            && uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string value) => IsRegistryName(value) || IsDescriptorUri(value);
}
