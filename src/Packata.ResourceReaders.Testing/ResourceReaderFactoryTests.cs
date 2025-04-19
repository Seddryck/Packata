using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders.Inference;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders.Testing;
public class ResourceReaderFactoryTests
{
    [Test]
    public void Create_TableDelimited_ReturnsTableDelimitedReader()
    {
        var resource = new Resource() { Type = "table", Dialect= new TableDelimitedDialect() { Type = "delimited" } };

        var factory = new ResourceReaderFactory();
        var reader = factory.Create(resource);
        Assert.That(reader, Is.InstanceOf<DelimitedReader>());
    }

    [Test]
    public void Create_TableDatabase_ReturnsTableDatabaseReader()
    {
        var resource = new Resource() { Type = "table", Dialect = new TableDatabaseDialect() { Type = "database" } };

        var factory = new ResourceReaderFactory();
        var reader = factory.Create(resource);
        Assert.That(reader, Is.InstanceOf<DatabaseReader>());
    }

    [Test]
    public void AddOrReplaceReader_NewTableDelimited_ReturnsTableDelimitedReader()
    {
        var resource = new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited" } };
        var delimitedReader = new Mock<IResourceReader>().Object;
        var builder = new Mock<IResourceReaderBuilder>();
        builder.Setup(b => b.Configure(It.IsAny<Resource>()));
        builder.Setup(b => b.Build()).Returns(delimitedReader);

        var factory = new ResourceReaderFactory();
        factory.AddOrReplaceReader("table", "delimited", builder.Object);
        var reader = factory.Create(resource);
        Assert.That(reader, Is.EqualTo(delimitedReader));
    }

    [Test]
    public void Create_ShouldEnrichDialect_WhenDialectIsNull()
    {
        var resource = new Resource { Type = "table", Dialect = null };
        var mockInferenceService = new Mock<IResourceInferenceService>();
        mockInferenceService
            .Setup(m => m.Enrich(It.IsAny<Resource>()))
            .Callback((Resource resource) =>
            {
                resource.Dialect = new TableDelimitedDialect
                {
                    Delimiter = '|',
                    QuoteChar = null,
                    DoubleQuote = false
                };
            });

        var factory = new ResourceReaderFactory(ResourceInferenceService.None);
        var reader = factory.Create(resource);
        Assert.That(resource.Dialect, Is.Null);

        factory = new ResourceReaderFactory(mockInferenceService.Object);
        reader = factory.Create(resource);
        Assert.That(resource.Dialect, Is.Not.Null);
        Assert.That(resource.Dialect, Is.TypeOf<TableDelimitedDialect>());

        var dialect = (TableDelimitedDialect)resource.Dialect;
        Assert.That(dialect.Delimiter, Is.EqualTo('|'));
        Assert.That(dialect.QuoteChar, Is.Null);
        Assert.That(dialect.DoubleQuote, Is.False);
        Assert.That(reader, Is.Not.Null);
    }
}
