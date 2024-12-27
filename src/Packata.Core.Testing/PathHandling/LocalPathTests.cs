using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.PathHandling;

namespace Packata.Core.Testing.PathHandling;
public class LocalPathTests
{
    [Test]
    public void ToStream_ExistingSTream_ReturnsIt()
    {
        var data = "foo;bar\\r\\n0;1";
        var fileSystem = new Mock<IFileSystem>();
        string filePath = "data.csv";
        fileSystem.Setup(fs => fs.Exists(filePath)).Returns(true);
        fileSystem.Setup(fs => fs.OpenRead(filePath)).Returns(new MemoryStream(Encoding.UTF8.GetBytes(data)));

        var path = new LocalPath(fileSystem.Object, "", filePath);
        Assert.That(path.ToStream(), Is.EqualTo(new MemoryStream(Encoding.UTF8.GetBytes(data))));
    }

    [Test]
    public void ToStream_NotFound_ThrowsFileNotFound()
    {
        var fileSystem = new Mock<IFileSystem>();
        string filePath = "data.csv";
        fileSystem.Setup(fs => fs.Exists(filePath)).Returns(false);

        var path = new LocalPath(fileSystem.Object, "", filePath);
        Assert.Throws<FileNotFoundException>(() => path.ToStream());
    }

    [Test]
    public void Exists_ExistingFile_ReturnsTrue()
    {
        var fileSystem = new Mock<IFileSystem>();
        string filePath = "data.csv";
        fileSystem.Setup(fs => fs.Exists(filePath)).Returns(true);

        var path = new LocalPath(fileSystem.Object, "", filePath);
        Assert.That(path.Exists(), Is.True);
    }

    [Test]
    public void Exists_NotFound_ReturnsFalse()
    {
        var fileSystem = new Mock<IFileSystem>();
        string filePath = "data.csv";
        fileSystem.Setup(fs => fs.Exists(filePath)).Returns(false);

        var path = new LocalPath(fileSystem.Object, "", filePath);
        Assert.That(path.Exists(), Is.False);
    }
}
