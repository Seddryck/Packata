namespace Packata.Arrow;

/// <summary>Limits adapter buffering, independently of any buffering performed by the source.</summary>
public sealed record ArrowBatchOptions
{
    public int BatchSize { get; init; } = 1024;
    /// <summary>Maximum logical Arrow buffer bytes per batch, including validity and offsets.
    /// A single row exceeding this limit is rejected. Managed bookkeeping and allocator padding are additional.</summary>
    public int MaxBufferedBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Keep the reader open after completion, failure, cancellation or early enumeration disposal.</summary>
    public bool LeaveOpen { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BatchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBufferedBytes);
    }
}
