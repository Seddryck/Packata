using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Storage;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference;

[TestFixture]
public class ExtensionBasedFormatInferenceTests
{
    [Test]
    [TestCase("csv", "csv")]
    [TestCase("tsv", "tsv")]
    [TestCase("psv", "psv")]
    [TestCase("parquet", "parquet")]
    [TestCase("pqt", "parquet")]
    [TestCase("xls", "xls")]
    [TestCase("xlsx", "xls")]
    public void TryInfer_ShouldReturnTrue_WhenExtensionIsKnownFormat(string extension, string expected)
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = extension;
                return true;
            });

        var inference = new ExtensionBasedFormatInference(extractor.Object);
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var format);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(format, Is.Not.Null);
        }
        Assert.That(format, Is.EqualTo(expected));
    }

    [Test]
    public void TryInfer_ShouldReturnFalse_WhenExtensionIsUnknown()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = "unknown";
                return true;
            });

        var inference = new ExtensionBasedFormatInference(extractor.Object);
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var format);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(format, Is.Null);
        }
    }

    [Test]
    public void TryInfer_ShouldReturnFalse_WhenNoExtensionIsExtracted()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns(false);

        var inference = new ExtensionBasedFormatInference(extractor.Object);
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var format);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(format, Is.Null);
        }
    }

    [Test]
    public void TryInfer_ShouldReturnFalse_WhenExtensionIsEmpty()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = "";
                return true;
            });

        var inference = new ExtensionBasedFormatInference(extractor.Object);
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var format);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(format, Is.Null);
        }
    }

    [Test]
    public void TryInfer_ShouldReturnFalse_WhenExtensionIsTooLong()
    {
        var extractor = new Mock<IExtractExtension>();
        extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string?>.IsAny))
            .Returns((IPath[] paths, out string? value) =>
            {
                value = "csv.tar.gz";
                return true;
            });

        var inference = new ExtensionBasedFormatInference(extractor.Object);
        var resource = new Resource();
        var result = inference.TryInfer(resource, out var format);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(format, Is.Null);
        }
    }
}

