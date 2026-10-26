namespace Packata.Storages;

/// <summary>The compression applied to the complete TAR stream.</summary>
public enum ArchiveCompression
{
    None,
    Gzip,
    Brotli
}
