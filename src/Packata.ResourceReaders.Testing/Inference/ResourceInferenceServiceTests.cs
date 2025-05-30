using NUnit.Framework;
using Packata.ResourceReaders.Inference;
using Moq;
using Packata.Core;

namespace Packata.ResourceReaders.Testing.Inference
{
    [TestFixture]
    public class ResourceInferenceServiceTests
    {
        [Test]
        public void Enrich_ShouldSetCompression_WhenCompressionIsNull()
        {
            var mockCompression = new Mock<ICompressionInference>();
            mockCompression
                .Setup(m => m.TryInfer(It.IsAny<Resource>(), out It.Ref<string?>.IsAny))
                .Returns((Resource resource, out string value) =>
                {
                    value = "gz";
                    return true;
                });

            var builder = new ResourceInferenceServiceBuilder();
            builder.AddStrategy(mockCompression.Object);
            var service = builder.Build();

            var resource = new Resource { Compression = null };
            service.Enrich(resource);

            Assert.That(resource.Compression, Is.EqualTo("gz"));
        }

        [Test]
        public void Enrich_ShouldNotSetCompression_WhenCompressionIsAlreadySet()
        {
            var service = ResourceInferenceServiceBuilder.None;

            var resource = new Resource { Compression = "gzip" };
            service.Enrich(resource);

            Assert.That(resource.Compression, Is.EqualTo("gzip"));
        }

        [Test]
        public void Enrich_ShouldSetDialect_WhenDialectIsNull()
        {
            var mockDialect = new Mock<IDialectInference>();
            mockDialect
                .Setup(m => m.TryInfer(It.IsAny<Resource>(), out It.Ref<TableDialect?>.IsAny))
                .Returns((Resource resource, out TableDialect? dialect) =>
                {
                    dialect = new TableDelimitedDialect { Delimiter = ',', QuoteChar = '"', DoubleQuote = true };
                    return true;
                });

            var builder = new ResourceInferenceServiceBuilder();
            builder.AddStrategy(mockDialect.Object);
            var service = builder.Build();

            var resource = new Resource { Dialect = null };
            service.Enrich(resource);

            Assert.That(resource.Dialect, Is.Not.Null);
            Assert.That(resource.Dialect, Is.TypeOf<TableDelimitedDialect>());
            var dialect = (TableDelimitedDialect)resource.Dialect;
            Assert.That(dialect.Delimiter, Is.EqualTo(','));
            Assert.That(dialect.QuoteChar, Is.EqualTo('"'));
            Assert.That(dialect.DoubleQuote, Is.True);
        }

        [Test]
        public void Enrich_ShouldSetFormat_WhenFormatIsNull()
        {
            var mockFormat = new Mock<IFormatInference>();
            mockFormat
                .Setup(m => m.TryInfer(It.IsAny<Resource>(), out It.Ref<string?>.IsAny))
                .Returns((Resource resource, out string? format) =>
                {
                    format = "foo";
                    return true;
                });

            var builder = new ResourceInferenceServiceBuilder();
            builder.AddStrategy(mockFormat.Object);
            var service = builder.Build();

            var resource = new Resource { Format = null };
            service.Enrich(resource);

            Assert.That(resource.Format, Is.Not.Null);
            Assert.That(resource.Format, Is.EqualTo("foo"));
        }

        [Test]
        public void Enrich_ShouldNotSetFormat_WhenFormatIsNotNull()
        {
            var mockFormat = new Mock<IFormatInference>();
            mockFormat
                .Setup(m => m.TryInfer(It.IsAny<Resource>(), out It.Ref<string?>.IsAny))
                .Returns((Resource resource, out string? format) =>
                {
                    format = "foo";
                    return true;
                });

            var builder = new ResourceInferenceServiceBuilder();
            builder.AddStrategy(mockFormat.Object);
            var service = builder.Build();

            var resource = new Resource { Format = "bar" };
            service.Enrich(resource);

            Assert.That(resource.Format, Is.EqualTo("bar"));
        }

        [Test]
        public void Enrich_ShouldNotSetDialect_WhenDialectIsAlreadySet()
        {
            var service = ResourceInferenceServiceBuilder.None;

            var resource = new Resource
            {
                Dialect = new TableDelimitedDialect { Delimiter = '\t', QuoteChar = '"', DoubleQuote = true }
            };
            service.Enrich(resource);

            Assert.That(resource.Dialect, Is.Not.Null);
            Assert.That(resource.Dialect, Is.TypeOf<TableDelimitedDialect>());
            var dialect = (TableDelimitedDialect)resource.Dialect;
            Assert.That(dialect.Delimiter, Is.EqualTo('\t'));
            Assert.That(dialect.QuoteChar, Is.EqualTo('"'));
            Assert.That(dialect.DoubleQuote, Is.True);
        }

        [Test]
        public void Enrich_ShouldSetKind_WhenKindIsNull()
        {
            var mockKind = new Mock<IKindInference>();
            mockKind
                .Setup(m => m.TryInfer(It.IsAny<Resource>(), out It.Ref<string?>.IsAny))
                .Returns((Resource resource, out string value) =>
                {
                    value = "mssql";
                    return true;
                });

            var builder = new ResourceInferenceServiceBuilder();
            builder.AddStrategy(mockKind.Object);
            var service = builder.Build();

            var resource = new Resource { Kind = null };
            service.Enrich(resource);

            Assert.That(resource.Kind, Is.EqualTo("mssql"));
        }

        [Test]
        public void Enrich_ShouldNotSetKind_WhenKindIsAlreadySet()
        {
            var service = ResourceInferenceServiceBuilder.None;

            var resource = new Resource { Kind = "service" };
            service.Enrich(resource);

            Assert.That(resource.Kind, Is.EqualTo("service"));
        }
    }
}

