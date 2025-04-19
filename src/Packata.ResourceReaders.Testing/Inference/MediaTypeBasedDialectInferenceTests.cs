using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class MediaTypeBasedDialectInferenceTests
    {
        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeIsNullOrEmpty()
        {
            var inference = new MediaTypeBasedDialectInference();
            var resource = new Resource { MediaType = null };

            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.False);
            Assert.That(dialect, Is.Null);
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeDoesNotStartWithText()
        {
            var inference = new MediaTypeBasedDialectInference();
            var resource = new Resource { MediaType = "application/json" };

            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.False);
            Assert.That(dialect, Is.Null);
        }

        [Test]
        [TestCase("text/csv", ',')]
        [TestCase("text/tsv", '\t')]
        [TestCase("text/tab-separated-values", '\t')]
        [TestCase("text/psv", '|')]
        public void TryInfer_ShouldReturnTrueAndSetDialect_WhenMediaTypeIsKnown(string mediaType, char delimiter)
        {
            var inference = new MediaTypeBasedDialectInference();
            var resource = new Resource { MediaType = mediaType };

            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.True);
            Assert.That(dialect, Is.Not.Null);
            Assert.That(dialect!.Delimiter, Is.EqualTo(delimiter));
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeIsUnknown()
        {
            var inference = new MediaTypeBasedDialectInference();
            var resource = new Resource { MediaType = "text/unknown" };

            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.False);
            Assert.That(dialect, Is.Null);
        }
    }
}
