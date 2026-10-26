namespace Packata.Storages;

// Also bounds TAR padding and metadata, which do not contribute to resource sizes.
internal sealed class BoundedArchiveStream(Stream source, long limit) : Stream
{
    public long BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        var read = source.Read(buffer[..Allowed(buffer.Length)]);
        Record(read);
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await source.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken).ConfigureAwait(false);
        Record(read);
        return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    private int Allowed(int count) => limit - BytesRead >= count ? count : (int)(limit - BytesRead + 1);
    private void Record(int count)
    {
        BytesRead += count;
        if (BytesRead > limit) throw new InvalidDataException("TAR decoded archive size limit exceeded.");
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
