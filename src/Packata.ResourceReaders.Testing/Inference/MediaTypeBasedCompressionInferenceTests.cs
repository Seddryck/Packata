using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class MediaTypeBasedCompressionInferenceTests
    {
        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeIsNullOrEmpty()
        {
            var inference = new MediaTypeBasedCompressionInference(new Dictionary<string, string> { { "gz", "gzip" } });
            var resource = new Resource { MediaType = null };

            var result = inference.TryInfer(resource, out var compression);

            Assert.That(result, Is.False);
            Assert.That(compression, Is.Null);
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeDoesNotStartWithApplication()
        {
            var inference = new MediaTypeBasedCompressionInference(new Dictionary<string, string> { { "gz", "gzip" } });
            var resource = new Resource { MediaType = "text/plain" };

            var result = inference.TryInfer(resource, out var compression);

            Assert.That(result, Is.False);
            Assert.That(compression, Is.Null);
        }

        [Test]
        public void TryInfer_ShouldReturnTrueAndSetCompression_WhenMediaTypeIsDeflate()
        {
            var inference = new MediaTypeBasedCompressionInference(
                new Dictionary<string, string> { { "gz", "gzip" }, { "deflate", "deflate" } });
            var resource = new Resource { MediaType = "application/x-deflate" };

            var result = inference.TryInfer(resource, out var compression);

            Assert.That(result, Is.True);
            Assert.That(compression, Is.EqualTo("deflate"));
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeIsCustom()
        {
            var inference = new MediaTypeBasedCompressionInference(
                new Dictionary<string, string> { { "gz", "gzip" }, { "deflate", "deflate" } });
            var resource = new Resource { MediaType = "application/custom" };

            var result = inference.TryInfer(resource, out var compression);

            Assert.That(result, Is.False);
        }
    }
}
