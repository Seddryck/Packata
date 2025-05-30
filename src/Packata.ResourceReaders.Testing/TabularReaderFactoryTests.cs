﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders.Tabular;
using PocketCsvReader;

namespace Packata.ResourceReaders.Testing;
public class TabularReaderFactoryTests
{
    [Test]
    public void Create_WithTableDelimited_ReturnsTableDelimitedReader()
    {
        var factory = new TabularReaderFactory();
        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited"} });
        Assert.That(reader, Is.InstanceOf<DelimitedReader>());
    }

    [Test]
    public void AddOrReplaceReader_SetDelimiter_ConfigureItBeforeBuilding()
    {
        var builder = new Mock<IResourceReaderBuilder>();
        builder.Setup(x => x.Configure(It.IsAny<Resource>()));
        builder.Setup(x => x.Build()).Returns(new DelimitedReader(new CsvReader()));

        var factory = new TabularReaderFactory();
        factory.AddOrReplaceReader(TabularReaderFactory.Delimited, builder.Object);
        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited", Delimiter=';' } });
        Assert.That(reader, Is.InstanceOf<DelimitedReader>());

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

        var factory = new TabularReaderFactory();
        factory.AddOrReplaceReader(TabularReaderFactory.Structured, builder.Object);
        factory.SetHeuristic(r => r.Dialect?.Type ?? TabularReaderFactory.Structured);

        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = "delimited", Delimiter = ';' } });
        Assert.That(reader, Is.InstanceOf<DelimitedReader>());

        reader = factory.Create(new Resource() { Type = "table" });
        Assert.That(reader, Is.EqualTo(structuredReader.Object));
    }

    [Test]
    [TestCase("delimited", typeof(DelimitedReader))]
    [TestCase("spreadsheet", typeof(SpreadsheetReader))]
    [TestCase("database", typeof(DatabaseReader))]
    public void Create_WithTableDialectType_ReturnsDelimitedReader(string dialectType, Type expected)
    {
        var factory = new TabularReaderFactory();
        var reader = factory.Create(new Resource() { Type = "table", Dialect = new TableDelimitedDialect() { Type = dialectType } });
        Assert.That(reader, Is.InstanceOf(expected));
    }

    [Test]
    [TestCase("csv", typeof(DelimitedReader))]
    [TestCase("tsv", typeof(DelimitedReader))]
    [TestCase("psv", typeof(DelimitedReader))]
    [TestCase("csv.gz", typeof(DelimitedReader))]
    [TestCase("tsv.gz", typeof(DelimitedReader))]
    [TestCase("psv.gz", typeof(DelimitedReader))]
    [TestCase("parquet", typeof(ParquetReader))]
    [TestCase("pqt", typeof(ParquetReader))]
    [TestCase("xls", typeof(SpreadsheetReader))]
    [TestCase("xlsx", typeof(SpreadsheetReader))]
    public void Create_WithTableFormat_ReturnsExpectedReader(string format, Type expected)
    {
        var factory = new TabularReaderFactory();
        var reader = factory.Create(new Resource() { Type = "table", Format = format });
        Assert.That(reader, Is.InstanceOf(expected));
    }
}
