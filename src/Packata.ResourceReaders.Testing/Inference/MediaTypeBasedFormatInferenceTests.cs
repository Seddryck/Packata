using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class MediaTypeBasedFormatInferenceTests
    {
        [Test]
        [TestCase("text/csv", "csv")]
        [TestCase("application/vnd.apache.parquet", "parquet")]
        [TestCase("application/vnd.ms-excel", "xls")]
        public void TryInfer_ShouldReturnTrue_WhenMediaTypeIsKnown(string mime, string expected)
        {
            var inference = new MediaTypeBasedFormatInference();
            var resource = new Resource { MediaType = mime };

            var result = inference.TryInfer(resource, out var format);

            Assert.That(result, Is.True);
            Assert.That(format, Is.EqualTo(expected));
        }

        [Test]
        [TestCase("text/plain")]
        [TestCase("application/unknown")]
        [TestCase(null)]
        public void TryInfer_ShouldReturnFalse_WhenMediaTypeIsUnknown(string? mime)
        {
            var inference = new MediaTypeBasedFormatInference();
            var resource = new Resource { MediaType = mime };

            var result = inference.TryInfer(resource, out var format);

            Assert.That(result, Is.False);
            Assert.That(format, Is.Null);
        }
        
        [Test]
        [TestCase("text/csv;charset=utf-8", "csv")]
        public void TryInfer_ShouldReturnTrue_WhenCharsetSpecifiedAndTypeKnown(string mime, string expected)
        {
            var inference = new MediaTypeBasedFormatInference();
            var resource = new Resource { MediaType = mime };

            var result = inference.TryInfer(resource, out var format);

            Assert.That(result, Is.True);
            Assert.That(format, Is.EqualTo(expected));
        }
    }
}
