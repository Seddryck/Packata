using NUnit.Framework;
using Packata.Core;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class FormatBasedDialectInferenceTests
    {
        [Test]
        public void TryInfer_ShouldReturnFalse_WhenFormatIsNullOrEmpty()
        {
            var inference = new FormatBasedDialectInference();
            var resource = new Resource { Format = null };

            var result = inference.TryInfer(resource, out var dialect);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.False);
                Assert.That(dialect, Is.Null);
            }
        }

        [Test]
        [TestCase("csv", ',', '"', null, true, "\r\n")]
        [TestCase("tsv", '\t', '"', null, true, "\r\n")]
        [TestCase("psv", '|', '"', '\\', false, "\r\n")]
        public void TryInfer_ShouldReturnTrueAndSetDialect_WhenFormatIsKnown(
            string format, char delimiter, char? quoteChar, char? escapeChar, bool doubleQuote, string lineTerminator)
        {
            var inference = new FormatBasedDialectInference();
            var resource = new Resource { Format = format };

            var result = inference.TryInfer(resource, out var dialect);

            Assert.That(result, Is.True);
            Assert.That(dialect, Is.Not.Null);
            Assert.That(dialect, Is.TypeOf<TableDelimitedDialect>());
            var tdDialect = (TableDelimitedDialect)dialect;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(tdDialect.Delimiter, Is.EqualTo(delimiter));
                Assert.That(tdDialect.QuoteChar, Is.EqualTo(quoteChar));
                Assert.That(tdDialect.EscapeChar, Is.EqualTo(escapeChar));
                Assert.That(tdDialect.DoubleQuote, Is.EqualTo(doubleQuote));
                Assert.That(tdDialect.LineTerminator, Is.EqualTo(lineTerminator));
            }
        }

        [Test]
        public void TryInfer_ShouldReturnFalse_WhenFormatIsUnknown()
        {
            var inference = new FormatBasedDialectInference();
            var resource = new Resource { Format = "unknown" };

            var result = inference.TryInfer(resource, out var dialect);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.False);
                Assert.That(dialect, Is.Null);
            }
        }
    }
}
