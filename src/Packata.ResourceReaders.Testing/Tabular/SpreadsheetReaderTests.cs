using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.PathHandling;
using Packata.Core.Testing.PathHandling;
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
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(fileStream);
        yield return new LocalPath(fileSystem.Object, "", "my-resource-path");
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
        Assert.That(dataReader, Is.InstanceOf<IExcelDataReader>());
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
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_ExistingLocalResourceWithName_ReturnsIDataReader(IPath path)
    {
        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect() { SheetNumber=null, SheetName = "Country" });
        var reader = new SpreadsheetReader(wrapper);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader, Is.InstanceOf<IExcelDataReader>());
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
    public void ToDataReader_FileNotFound_ThrowsFileNotFoundException()
    {
        // Setup a path that doesn't exist
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("non-existent-path")).Returns(false);
        var path = new LocalPath(fileSystem.Object, "", "non-existent-path");

        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);

        Assert.Throws<FileNotFoundException>(() => reader.ToDataReader(resource));
    }

    [Test]
    public void ToDataReader_IOError_ThrowsIOException()
    {
        // Setup a path that throws an IO exception when opened
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("error-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("error-path")).Throws<IOException>();
        var path = new LocalPath(fileSystem.Object, "", "error-path");

        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ExcelReaderWrapper(new TableSpreadsheetDialect());
        var reader = new SpreadsheetReader(wrapper);

        Assert.Throws<IOException>(() => reader.ToDataReader(resource));
    }
}
