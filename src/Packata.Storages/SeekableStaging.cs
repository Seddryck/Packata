namespace Packata.Storages;

/// <summary>Bounded disk staging, independent of transport and data format.</summary>
public static class SeekableStaging
{
    public const long DefaultMaximumBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Borrows the input by default. The lease owns only a newly staged file; with leaveOpen=false
    /// it takes ownership of the input even on failure. Seekable inputs pass through without copying.
    /// </summary>
    public static async Task<SeekableInput> AcquireAsync(Stream source, long maximumBytes = DefaultMaximumBytes,
        bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Stream? staged = null;
        var ownsSource = !leaveOpen;
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.CanRead) throw new ArgumentException("A readable resource stream is required.", nameof(source));
            if (source.CanSeek) return new SeekableInput(source, ownsSource);
            staged = await CopyAsync(source, maximumBytes, cancellationToken).ConfigureAwait(false);
            if (ownsSource)
            {
                ownsSource = false;
                await source.DisposeAsync().ConfigureAwait(false);
            }
            return new SeekableInput(staged, ownsStream: true);
        }
        catch
        {
            if (staged is not null) await CleanupAfterFailureAsync(staged).ConfigureAwait(false);
            if (ownsSource) await CleanupAfterFailureAsync(source).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Takes ownership of the input, returning one owned seekable stream (or closing it on failure).</summary>
    public static async Task<Stream> OpenOwnedAsync(Stream source, long maximumBytes = DefaultMaximumBytes,
        CancellationToken cancellationToken = default)
    {
        await using var input = await AcquireAsync(source, maximumBytes, leaveOpen: false, cancellationToken).ConfigureAwait(false);
        return input.Release();
    }

    /// <summary>Always copies; leaves the caller's input open. The returned temporary file is deleted on close.</summary>
    public static async Task<FileStream> CopyAsync(Stream source, long maximumBytes = DefaultMaximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        CheckKnownLength(source, maximumBytes);
        var path = Path.Combine(Path.GetTempPath(), $"packata-stage-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            BufferSize = 81920, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var file = new FileStream(path, options);
        try
        {
            await CopyBoundedAsync(source, file, maximumBytes, cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            return file;
        }
        catch
        {
            await CleanupAfterFailureAsync(file).ConfigureAwait(false);
            throw;
        }
    }

    private static void CheckKnownLength(Stream source, long maximumBytes)
    {
        try { if (source.Length > maximumBytes) throw new StagingLimitExceededException(maximumBytes); }
        catch (NotSupportedException) { /* Unknown length: enforce the limit while reading. */ }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = maximumBytes - total;
            var allowed = remaining >= buffer.Length ? buffer.Length : (int)(remaining + 1);
            var count = await source.ReadAsync(buffer.AsMemory(0, allowed), cancellationToken).ConfigureAwait(false);
            if (count == 0) return;
            total += count;
            if (total > maximumBytes) throw new StagingLimitExceededException(maximumBytes);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask CleanupAfterFailureAsync(IAsyncDisposable resource)
    {
        try { await resource.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* Preserve the acquisition failure, including cancellation, if cleanup also fails. */ }
    }
}
