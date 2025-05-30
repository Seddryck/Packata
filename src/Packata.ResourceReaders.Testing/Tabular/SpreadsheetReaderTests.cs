﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Storage;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders.Testing.Tabular;
public class SpreadsheetReaderTests
{
    [OneTimeSetUp()]
    public void Setup()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static IEnumerable<IPath> GetPaths()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"{typeof(ResourceTests).Namespace}.Resources.my-book.xlsx")
            ?? throw new FileNotFoundException("Resource not found", $"{typeof(ResourceTests).Namespace}.Resources.my-book.xlsx");

        var fileStream = new MemoryStream();
        stream.CopyTo(fileStream);
        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(true);
        path.Setup(x => x.OpenAsync()).ReturnsAsync(fileStream);
        yield return path.Object;
    }

    [Test]
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader(IPath path)
    {
        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(1));
            Assert.That(dataReader[1], Is.EqualTo("alpha"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(2));
            Assert.That(dataReader[1], Is.EqualTo("beta"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(3));
            Assert.That(dataReader[1], Is.EqualTo("gamma"));
        }
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_NoResource_Throws()
    {
        var resource = new Resource() { Paths = [], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);
        Assert.Throws<InvalidOperationException>(() => reader.ToDataReader(resource));
    }

    [Test]
    public void ToDataReader_TwoResources_Throws()
    {
        var resource = new Resource() { Paths = [new Mock<IPath>().Object, new Mock<IPath>().Object], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);
        Assert.Throws<InvalidOperationException>(() => reader.ToDataReader(resource));
    }

    [Test]
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_ExistingLocalResourceWithName_ReturnsIDataReader(IPath path)
    {
        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect() { SheetNumber = null, SheetName = "Country", Header = true });
        var reader = new SpreadsheetReader(wrapper);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo("be"));
            Assert.That(dataReader[1], Is.EqualTo("Belgium"));
            Assert.That(dataReader[2], Is.EqualTo("Brussels"));
        }
        for (int i = 1; i < 5; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_ExistingLocalResourceWithName_ReturnsFieldNames(IPath path)
    {
        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect() { SheetNumber = null, SheetName = "Country", Header = true });
        var reader = new SpreadsheetReader(wrapper);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader.GetName(0), Is.EqualTo("Code"));
            Assert.That(dataReader.GetName(1), Is.EqualTo("Name"));
            Assert.That(dataReader.GetName(2), Is.EqualTo("Capital"));

            Assert.That(dataReader.GetOrdinal("Code"), Is.EqualTo(0));
            Assert.That(dataReader.GetOrdinal("Name"), Is.EqualTo(1));
            Assert.That(dataReader.GetOrdinal("Capital"), Is.EqualTo(2));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["Code"], Is.EqualTo("be"));
            Assert.That(dataReader["Name"], Is.EqualTo("Belgium"));
            Assert.That(dataReader["Capital"], Is.EqualTo("Brussels"));
        }
        for (int i = 1; i < 5; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_FileNotFound_ThrowsFileNotFoundException()
    {
        // Setup a path that doesn't exist
        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(false);

        var resource = new Resource() { Paths = [path.Object], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);

        Assert.Throws<FileNotFoundException>(() => reader.ToDataReader(resource));
    }

    [Test]
    public void ToDataReader_IOError_ThrowsIOException()
    {
        // Setup a path that throws an IO exception when opened
        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(true);
        path.Setup(x => x.OpenAsync()).ThrowsAsync(new IOException());

        var resource = new Resource() { Paths = [path.Object], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);

        Assert.Throws<IOException>(() => reader.ToDataReader(resource));
    }
}
