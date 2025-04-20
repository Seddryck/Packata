using System;
using System.Collections.Generic;
using System.IO;
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
public class ParquetReaderTests
{
    
    private static IEnumerable<IPath> GetPaths()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"{typeof(ResourceTests).Namespace}.Resources.iris.parquet")
            ?? throw new FileNotFoundException("Resource not found", $"{typeof(ResourceTests).Namespace}.Resources.iris.parquet");

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
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(5.10));
            Assert.That(dataReader[4], Is.EqualTo("setosa"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(4.90));
            Assert.That(dataReader[4], Is.EqualTo("setosa"));
        }
        for (int i = 0; i < 148; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_NoResource_Throws()
    {
        var resource = new Resource() { Paths = [], Type = "table", Name = "my-resource" };
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);
        Assert.Throws<InvalidOperationException>(() => reader.ToDataReader(resource));
    }

    [Test]
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_TwoResources_DataReader(IPath path)
    {
        var resource = new Resource() { Paths = [path, path], Type = "table", Name = "my-resource" };
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);
        var dr = reader.ToDataReader(resource);
        Assert.That(dr, Is.Not.Null);
        Assert.That(dr, Is.TypeOf<ParquetDataReader>());
    }

    [Test]
    public void ToDataReader_FileNotFound_ThrowsFileNotFoundException()
    {
        // Setup a path that doesn't exist
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("non-existent-path")).Returns(false);
        var path = new LocalPath(fileSystem.Object, "", "non-existent-path");

        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);

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
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);

        Assert.Throws<IOException>(() => reader.ToDataReader(resource));
    }
}
