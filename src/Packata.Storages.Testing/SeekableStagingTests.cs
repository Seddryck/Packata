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
            await Assert.ThrowsAsync<StagingLimitExceededException>(async () => await SeekableStaging.CopyAsync(source, 4));
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

    [TestCase(true)]
    [TestCase(false)]
    public async Task Acquire_SeekablePassesThroughWithoutReadingAndRespectsOwnership(bool leaveOpen)
    {
        var source = new CountedInput([1, 2, 3], seekable: true) { Position = 1 };
        var lease = await SeekableStaging.AcquireAsync(source, 1, leaveOpen);
        Assert.That(lease.Stream, Is.SameAs(source));
        Assert.That(source.Position, Is.EqualTo(1));
        Assert.That(source.Reads, Is.Zero);
        await lease.DisposeAsync();
        lease.Dispose();
        Assert.That(source.Disposals, Is.EqualTo(leaveOpen ? 0 : 1));
        if (leaveOpen) source.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Acquire_NonSeekablePartialReadsStageOnceAndRespectOwnership(bool leaveOpen)
    {
        var source = new CountedInput([1, 2, 3, 4]);
        var lease = await SeekableStaging.AcquireAsync(source, 4, leaveOpen);
        var staged = (FileStream)lease.Stream;
        var path = staged.Name;
        Assert.That(staged.Length, Is.EqualTo(4));
        Assert.That(staged.ReadByte(), Is.EqualTo(1));
        staged.Position = 3;
        Assert.That(staged.ReadByte(), Is.EqualTo(4));
        await lease.DisposeAsync();
        lease.Dispose();
        Assert.That(File.Exists(path), Is.False);
        Assert.That(source.Disposals, Is.EqualTo(leaveOpen ? 0 : 1));
        if (leaveOpen) source.Dispose();
    }

    [TestCase(null, 3)]
    [TestCase(1L, 3)]
    [TestCase(5L, 0)]
    public async Task Acquire_EnforcesUnknownIncorrectAndKnownLengths(long? length, int expectedReads)
    {
        var source = new CountedInput([1, 2, 3, 4, 5]) { DeclaredLength = length };
        var before = TemporaryFiles();
        await Assert.ThrowsAsync<StagingLimitExceededException>(async () => await SeekableStaging.AcquireAsync(source, 4, leaveOpen: false));
        Assert.That(source.Reads, Is.EqualTo(expectedReads));
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    [Test]
    public async Task Acquire_PreservesTransferFailureWhenSourceCleanupFails()
    {
        var primary = new IOException("original transfer failure");
        var source = new CountedInput([1, 2, 3]) { ReadFailure = primary, FailDisposal = true };
        var before = TemporaryFiles();
        var failure = await Assert.ThrowsAsync<IOException>(async () => await SeekableStaging.AcquireAsync(source, 4, leaveOpen: false));
        Assert.That(failure, Is.SameAs(primary));
        Assert.That(source.Disposals, Is.EqualTo(1));
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Acquire_CancellationBeforeReadingHonorsOwnership(bool leaveOpen)
    {
        var source = new CountedInput([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThatAsync(async () => await SeekableStaging.AcquireAsync(source, 4, leaveOpen, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Reads, Is.Zero);
        Assert.That(source.Disposals, Is.EqualTo(leaveOpen ? 0 : 1));
        if (leaveOpen) source.Dispose();
    }

    [Test]
    public async Task Acquire_ConcurrentOperationsUseUniqueFilesAndDeleteAfterParserFailure()
    {
        var before = TemporaryFiles();
        var leases = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            await SeekableStaging.AcquireAsync(new CountedInput([1, 2, 3]), 3, leaveOpen: false)));
        var paths = leases.Select(lease => ((FileStream)lease.Stream).Name).ToArray();
        Assert.That(paths.Distinct().Count(), Is.EqualTo(12));
        foreach (var lease in leases)
        {
            await Assert.ThrowsAsync<FormatException>(async () =>
            {
                await using (lease) throw new FormatException("parser failed");
            });
        }
        Assert.That(paths.Any(File.Exists), Is.False);
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    private static string[] TemporaryFiles() => Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp");

    private sealed class CountedInput(byte[] bytes, bool seekable = false) : MemoryStream(bytes)
    {
        public int Disposals { get; private set; }
        public int Reads { get; private set; }
        public long? DeclaredLength { get; init; }
        public IOException? ReadFailure { get; init; }
        public bool FailDisposal { get; init; }
        public override bool CanSeek => seekable;
        public override long Length => DeclaredLength ?? (seekable ? base.Length : throw new NotSupportedException());
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (ReadFailure is not null) throw ReadFailure;
            return base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            Disposals++;
            base.Dispose(disposing);
            if (FailDisposal) throw new IOException("cleanup failed");
        }
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
