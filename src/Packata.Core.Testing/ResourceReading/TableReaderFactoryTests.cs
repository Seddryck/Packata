using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.ResourceReading;
using PocketCsvReader;

namespace Packata.Core.Testing.ResourceReading;
public class TableReaderFactoryTests
{
    [Test]
    public void Create_WithTableDelimited_ReturnsTableDelimitedReader()
    {
        var factory = new TableReaderFactory();
        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited"} });
        Assert.That(reader, Is.InstanceOf<TableDelimitedReader>());
    }

    [Test]
    public void AddOrReplaceReader_SetDelimiter_ConfigureItBeforeBuilding()
    {
        var builder = new Mock<IResourceReaderBuilder>();
        builder.Setup(x => x.Configure(It.IsAny<Resource>()));
        builder.Setup(x => x.Build()).Returns(new TableDelimitedReader(new CsvReader()));

        var factory = new TableReaderFactory();
        factory.AddOrReplaceReader(TableReaderFactory.Delimited, builder.Object);
        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited", Delimiter=';' } });
        Assert.That(reader, Is.InstanceOf<TableDelimitedReader>());

        builder.Verify(x => x.Configure(It.Is<Resource>(r => (r.Dialect as TableDelimitedDialect)!.Delimiter == ';')), Times.Once);
        builder.Verify(x => x.Build(), Times.Once);
    }

    [Test]
    public void SetHeuristic_UseDialectProperties_ApplyHeuristics()
    {
        var structuredReader = new Mock<IResourceReader>();
        var builder = new Mock<IResourceReaderBuilder>();
        builder.Setup(x => x.Configure(It.IsAny<Resource>()));
        builder.Setup(x => x.Build()).Returns(structuredReader.Object);

        var factory = new TableReaderFactory();
        factory.AddOrReplaceReader(TableReaderFactory.Structured, builder.Object);
        factory.SetHeuristic(r => r.Dialect?.Type ?? TableReaderFactory.Structured);

        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited", Delimiter = ';' } });
        Assert.That(reader, Is.InstanceOf<TableDelimitedReader>());

        reader = factory.Create(new Resource() { Type = "table" });
        Assert.That(reader, Is.EqualTo(structuredReader.Object));
    }
}
