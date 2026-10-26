namespace Packata.Storages;

/// <summary>A transfer/staging limit, independent of parser metadata and sample limits.</summary>
public sealed class StagingLimitExceededException(long maximumBytes)
    : IOException($"Resource staging byte limit exceeded ({maximumBytes} bytes).")
{
    public long MaximumBytes { get; } = maximumBytes;
}
