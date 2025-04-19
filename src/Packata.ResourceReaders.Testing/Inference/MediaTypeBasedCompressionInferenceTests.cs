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
        [TestCase("application/x-deflate", "deflate")]
        [TestCase("application/gzip", "gz")]
        [TestCase("application/zip", "zip")]
        public void TryInfer_ShouldReturnTrueAndSetCompression_WhenMediaTypeIsDeflate(string mime, string expected)
        {
            var inference = new MediaTypeBasedCompressionInference(
                new Dictionary<string, string> { { "gzip", "gz" }, { "zip", "zip" }, { "deflate", "deflate" } });
            var resource = new Resource { MediaType = mime };

            var result = inference.TryInfer(resource, out var compression);

            Assert.That(result, Is.True);
            Assert.That(compression, Is.EqualTo(expected));
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
