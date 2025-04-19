using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class ExtensionBasedDialectInferenceTests
    {
        [Test]
        [TestCase("csv")]
        [TestCase("tsv")]
        public void TryInfer_ShouldReturnTrue_WhenExtensionIsKnownFormat(string extension)
        {
            var extractor = new Mock<IExtractExtension>();
            extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string>.IsAny))
                .Returns((IPath[] paths, out string value) =>
                {
                    value = extension;
                    return true;
                });

            var inference = new ExtensionBasedDialectInference(extractor.Object);
            var resource = new Resource();
            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.True);
            Assert.That(dialect, Is.Not.Null);
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenExtensionIsUnknown()
        {
            var extractor = new Mock<IExtractExtension>();
            extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string>.IsAny))
                .Returns((IPath[] paths, out string value) =>
                {
                    value = "unknown";
                    return true;
                });

            var inference = new ExtensionBasedDialectInference(extractor.Object);
            var resource = new Resource();
            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.False);
            Assert.That(dialect, Is.Null);
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenNoExtensionIsExtracted()
        {
            var extractor = new Mock<IExtractExtension>();
            extractor.Setup(e => e.TryGetPathExtension(It.IsAny<IPath[]>(), out It.Ref<string>.IsAny))
                .Returns(false);

            var inference = new ExtensionBasedDialectInference(extractor.Object);
            var resource = new Resource();
            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.False);
            Assert.That(dialect, Is.Null);
        }
    }
}

