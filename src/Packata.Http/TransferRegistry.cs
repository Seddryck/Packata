using System.Security.Cryptography;

namespace Packata.Http;

public sealed record TransferStatus(string Id, string AssetId, string State, long Bytes, string? Sha256,
    string? ErrorCode = null, string? Field = null);

internal sealed class TransferRegistry(int capacity, TimeSpan retention)
{
    private readonly object gate = new();
    private readonly Dictionary<string, (TransferStatus Status, DateTimeOffset Expires)> entries = [];

    public TransferStatus? Create(string assetId)
    {
        lock (gate)
        {
            foreach (var key in entries.Where(entry => entry.Value.Expires < DateTimeOffset.UtcNow).Select(entry => entry.Key).ToArray())
                entries.Remove(key);
            if (entries.Count >= capacity) return null;
            var status = new TransferStatus(Guid.NewGuid().ToString("N"), assetId, "pending", 0, null);
            entries.Add(status.Id, (status, DateTimeOffset.MaxValue));
            return status;
        }
    }

    public void Finish(string id, string state, long bytes, string? digest, string? errorCode = null, string? field = null)
    {
        lock (gate)
        {
            var status = entries[id].Status with { State = state, Bytes = bytes, Sha256 = digest, ErrorCode = errorCode, Field = field };
            entries[id] = (status, DateTimeOffset.UtcNow + retention);
        }
    }

    public TransferStatus? Find(string id)
    {
        lock (gate)
            return entries.TryGetValue(id, out var entry) && entry.Expires >= DateTimeOffset.UtcNow ? entry.Status : null;
    }
}

/// <summary>Counts and hashes only successfully awaited data writes. Never closes the HTTP body.</summary>
internal sealed class TransferStream(Stream inner) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long Bytes { get; private set; }
    public string Digest() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        hash.AppendData(buffer.Span);
        Bytes += buffer.Length;
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException("Use asynchronous writes.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous writes.");
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
