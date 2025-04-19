using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.PathHandling;
using Packata.Core.Testing.PathHandling;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders.Testing.Tabular;
public class SpreadsheetReaderBuilderTests
{
    private static LocalPath GetPath()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"{typeof(ResourceTests).Namespace}.Resources.my-book.xlsx")
            ?? throw new FileNotFoundException("Resource not found", $"{typeof(ResourceTests).Namespace}.Resources.my-book.xlsx");

        var fileStream = new MemoryStream();
        stream.CopyTo(fileStream);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(fileStream);
        return new LocalPath(fileSystem.Object, "", "my-resource-path");
    }

    [Test]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader()
    {
        var resource = new Resource
        {
            Paths = [GetPath()],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableSpreadsheetDialect() { SheetNumber = 2, Header = false, HeaderRows = [] }
        };
        var builder = new SpreadsheetReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo("Code"));
            Assert.That(dataReader.GetValue(0), Is.EqualTo("Code"));
            Assert.That(dataReader[1], Is.EqualTo("Name"));
            Assert.That(dataReader[2], Is.EqualTo("Capital"));
        }
        for (int i = 0; i < 5; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_ExistingLocalResourceSheetNotExistingName_Throws()
    {
        var resource = new Resource
        {
            Paths = [GetPath()],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableSpreadsheetDialect() { SheetName = "Unknown" }
        };
        var builder = new SpreadsheetReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        Assert.Throws<InvalidOperationException>(() => reader.ToDataReader(resource));
    }

    [Test]
    [TestCase(3)]
    [TestCase(4)]
    public void ToDataReader_ExistingLocalResourceSheetNotExistingNumber_Throws(int sheetNumber)
    {
        var resource = new Resource
        {
            Paths = [GetPath()],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableSpreadsheetDialect() { SheetNumber = sheetNumber }
        };
        var builder = new SpreadsheetReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        Assert.Throws<InvalidOperationException>(() => reader.ToDataReader(resource));
    }

    [Test]
    public void ToDataReader_BothSheetNameAndNumberProvided_Throws()
    {
        var resource = new Resource
        {
            Paths = [GetPath()],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableSpreadsheetDialect() { SheetNumber = 1, SheetName = "Sheet1" }
        };
        var builder = new SpreadsheetReaderBuilder();

        Assert.Throws<AggregateException>(() => builder.Configure(resource));
    }
}
