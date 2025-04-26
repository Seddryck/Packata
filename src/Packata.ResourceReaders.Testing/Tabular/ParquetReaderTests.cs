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
using Packata.Core.Storage;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders.Testing.Tabular;
public class ParquetReaderTests
{
    private static IEnumerable<IPath> GetPaths()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"{typeof(ResourceTests).Namespace}.Resources.iris.parquet")
            ?? throw new FileNotFoundException("Resource not found", $"{typeof(ResourceTests).Namespace}.Resources.iris.parquet");

        using var tmp = new MemoryStream();
        stream.CopyTo(tmp);
        var parquetBytes = tmp.ToArray();// cache the bytes once

        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(true);
        path.Setup(x => x.OpenAsync()).ReturnsAsync(() => new MemoryStream(parquetBytes, writable: false)); // fresh stream
        yield return path.Object;
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
        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(false);

        var resource = new Resource() { Paths = [path.Object], Type = "table", Name = "my-resource" };
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);

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
        var wrapper = new ParquetReaderWrapper();
        var reader = new ParquetReader(wrapper);

        Assert.Throws<IOException>(() => reader.ToDataReader(resource));
    }
}
