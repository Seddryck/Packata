using NUnit.Framework;

namespace Packata.Storages.Testing;

public sealed class SeekableStagingTests
{
    [Test]
    public async Task Stage_IsSeekableRetainsInputOwnershipAndDeletesOnClose()
    {
        using var input = new MemoryStream([1, 2, 3]);
        var file = await SeekableStaging.CopyAsync(input, 3);
        var path = file.Name;
        Assert.That(file.CanSeek, Is.True);
        Assert.That(file.ReadByte(), Is.EqualTo(1));
        await file.DisposeAsync();
        Assert.That(File.Exists(path), Is.False);
        Assert.That(input.CanRead, Is.True);
    }

    [TestCase("limit")]
    [TestCase("cancellation")]
    [TestCase("failure")]
    public async Task Stage_BoundsUnknownLengthAndCleansUpOnFailure(string outcome)
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp");
        using var cancellation = new CancellationTokenSource();
        using var source = new Input(outcome, cancellation);
        if (outcome == "cancellation")
            await Assert.ThatAsync(async () => await SeekableStaging.CopyAsync(source, 4, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        else if (outcome == "failure")
            await Assert.ThrowsAsync<IOException>(async () => await SeekableStaging.CopyAsync(source, 4));
        else
            await Assert.ThrowsAsync<InvalidDataException>(async () => await SeekableStaging.CopyAsync(source, 4));
        Assert.That(Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp"), Is.EquivalentTo(before));
        Assert.That(source.Reads, Is.LessThanOrEqualTo(5));
    }

    [Test]
    public void BoundedStream_CancelsSynchronousReadsAndRejectsOverflow()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new BoundedReadStream(new MemoryStream([1, 2]), 1, cancellation.Token);
        Assert.That(stream.ReadByte(), Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => stream.ReadByte());
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => stream.ReadByte());
    }

    private sealed class Input(string outcome, CancellationTokenSource cancellation) : MemoryStream
    {
        public int Reads { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++Reads == 2 && outcome == "failure") throw new IOException("transfer failed");
            buffer.Span[0] = 1;
            if (outcome == "cancellation") cancellation.Cancel();
            return ValueTask.FromResult(1);
        }
    }
}
