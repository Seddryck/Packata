namespace Packata.Storages;

/// <summary>Bounded disk staging for consumers that require seeking. The caller owns the input.</summary>
public static class SeekableStaging
{
    public const long DefaultMaximumBytes = 256L * 1024 * 1024;

    /// <summary>Returns an owned temporary stream deleted on close, including after copy failure or cancellation.</summary>
    public static async Task<FileStream> CopyAsync(Stream source, long maximumBytes = DefaultMaximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(Path.GetTempPath(), $"packata-stage-{Guid.NewGuid():N}.tmp");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(0,
                maximumBytes - total >= buffer.Length ? buffer.Length : (int)(maximumBytes - total + 1)), cancellationToken).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > maximumBytes) throw new InvalidDataException("Download/staging byte limit exceeded.");
                await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            file.Position = 0;
            return file;
        }
        catch { await file.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
