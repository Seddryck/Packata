using System.Data;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public sealed class ResourceOwnershipTests
{
    [Test]
    public void OwnedReader_ReleasesEveryResourceAndPreservesFirstDisposalFailure()
    {
        var original = new IOException("reader cleanup failed");
        var inner = new Mock<IDataReader>();
        inner.Setup(reader => reader.Dispose()).Throws(original);
        var first = new ThrowingStream();
        var second = new MemoryStream();
        var reader = new OwnedDataReader(inner.Object, first, second);
        var failure = Assert.Throws<IOException>(reader.Dispose);
        Assert.That(failure, Is.SameAs(original));
        Assert.That(second.CanRead, Is.False);
        reader.Dispose();
        Assert.That(first.Disposals, Is.EqualTo(1));
        inner.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task Factory_PreservesParserExceptionWhenUpstreamCleanupFails()
    {
        var primary = new FormatException("original parser failure");
        var source = new ThrowingStream();
        var streams = new Mock<IEndpointStreamResolver>();
        streams.Setup(value => value.OpenAsync("data.bin", It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var provider = new Mock<IDataEndpointReaderProvider>();
        provider.Setup(value => value.CanHandle(It.IsAny<DataEndpointReadRequest>(), It.IsAny<ResolvedDataFormat>())).Returns(true);
        provider.Setup(value => value.OpenAsync(It.IsAny<ReaderOpenContext>(), It.IsAny<CancellationToken>())).ThrowsAsync(primary);
        var factory = new ResourceReaderFactory(streams.Object, [provider.Object]);
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null, new PathLocation(["data.bin"]), new DataFormat("bin"));
        var failure = await Assert.ThrowsAsync<FormatException>(async () => await factory.OpenAsync(endpoint));
        Assert.That(failure, Is.SameAs(primary));
        Assert.That(source.Disposals, Is.EqualTo(1));
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public int Disposals { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposals++;
            base.Dispose(disposing);
            throw new IOException("upstream cleanup failed");
        }
    }
}
