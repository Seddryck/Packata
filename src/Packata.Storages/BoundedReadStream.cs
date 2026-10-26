namespace Packata.Storages;

/// <summary>Owns an input stream and enforces a byte limit and operation cancellation on every read.</summary>
public sealed class BoundedReadStream(Stream source, long maximumBytes, CancellationToken operationCancellation = default,
    IDisposable? owner = null) : Stream
{
    private long _read;
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override int Read(Span<byte> buffer)
    {
        var bytes = new byte[Math.Min(buffer.Length, 81920)];
        var count = Read(bytes, 0, bytes.Length);
        bytes.AsSpan(0, count).CopyTo(buffer);
        return count;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        var remaining = maximumBytes - _read;
        var allowed = remaining >= buffer.Length ? buffer.Length : (int)Math.Max(1, remaining + 1);
        var count = await source.ReadAsync(buffer[..allowed], linked.Token).ConfigureAwait(false);
        _read += count;
        if (_read > maximumBytes) throw new InvalidDataException("Download/staging byte limit exceeded.");
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { try { source.Dispose(); } finally { owner?.Dispose(); } }
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        try { await source.DisposeAsync().ConfigureAwait(false); }
        finally { owner?.Dispose(); }
        GC.SuppressFinalize(this);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
