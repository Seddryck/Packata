namespace Packata.Storages;

/// <summary>Limits for materializing a TAR archive. Sizes are measured in bytes.</summary>
public sealed record TarArchiveOptions
{
    public int MaxEntries { get; init; } = 10_000;
    public long MaxEntryBytes { get; init; } = 1L << 30;
    public long MaxTotalBytes { get; init; } = 4L << 30;
    /// <summary>Bounds the entire decoded TAR stream, including headers, metadata, and padding.</summary>
    public long MaxArchiveBytes { get; init; } = 5L << 30;
    /// <summary>Maximum combined entry content retained in memory; larger contents spill to disk.</summary>
    public long MemoryThresholdBytes { get; init; } = 8L << 20;
    /// <summary>A private existing spill directory, or null to use Packata's private per-user directory.</summary>
    public string? TemporaryDirectory { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MemoryThresholdBytes);
        if (TemporaryDirectory is not null) ArgumentException.ThrowIfNullOrWhiteSpace(TemporaryDirectory);
    }
}
