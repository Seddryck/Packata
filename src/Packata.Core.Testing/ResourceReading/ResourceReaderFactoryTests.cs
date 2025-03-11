using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.ResourceReading;

namespace Packata.Core.Testing.ResourceReading;
public class ResourceReaderFactoryTests
{
    [Test]
    public void Create_TableDelimited_ReturnsTableDelimitedReader()
    {
        var resource = new Resource() { Type = "table", Dialect= new TableDelimitedDialect() { Type = "delimited" } };

        var factory = new ResourceReaderFactory();
        var reader = factory.Create(resource);
        Assert.That(reader, Is.InstanceOf<TableDelimitedReader>());
    }

    [Test]
    public void Create_TableDatabase_ReturnsTableDatabaseReader()
    {
        var resource = new Resource() { Type = "table", Dialect = new TableDatabaseDialect() { Type = "database" } };

        var factory = new ResourceReaderFactory();
        var reader = factory.Create(resource);
        Assert.That(reader, Is.InstanceOf<TableDatabaseReader>());
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
}
