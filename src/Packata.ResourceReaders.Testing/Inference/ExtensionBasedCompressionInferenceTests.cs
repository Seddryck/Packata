using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Storage;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference;

[TestFixture]
public class ExtensionBasedCompressionInferenceTests
{
    [Test]
    [TestCase("gz")]
    [TestCase("csv.gz")]
    [TestCase("gzip")]
    public void TryInferFromExtension_ShouldReturnTrue_WhenExtensionIsGzip(string extension)
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = extension;
                return true;
            });

        var inference = new ExtensionBasedCompressionInference(
            extractor.Object
            , new Dictionary<string, string> { {"gz", "gzip" }, { "gzip", "gzip" } }
        );
        var path = new Mock<IPath>();
        path.SetupGet(p => p.RelativePath).Returns($"file.{extension}");
        var resource = new Resource() { Paths = [path.Object] };
        var result = inference.TryInfer(resource, out var compression);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(compression, Is.EqualTo("gzip"));
        }
    }

    [Test]
    public void TryInferFromExtension_ShouldReturnFalse_WhenExtensionIsUnknown()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = "foo";
                return true;
            });

        var inference = new ExtensionBasedCompressionInference(
            extractor.Object
            , new Dictionary<string, string> { { "gz", "gzip" } }
        );
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var compression);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(compression, Is.Null);
        }
    }

    [Test]
    public void TryInfer_ShouldReturnFalse_WhenExtractorFails()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns(false);

        var inference = new ExtensionBasedCompressionInference(
            extractor.Object,
            new Dictionary<string, string> { { "gz", "gzip" } }
        );
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var compression);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(compression, Is.Null);
        }
    }
}
