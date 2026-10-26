namespace Packata.Storages;

internal static class ArchiveSource
{
    public static string Path(Uri uri) => uri.Scheme is "http" or "https"
        ? uri.AbsoluteUri
        : Uri.UnescapeDataString(uri.Segments[^1]);
}
