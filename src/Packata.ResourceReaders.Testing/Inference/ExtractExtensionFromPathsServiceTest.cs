using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.ResourceReaders.Inference;

namespace Packata.ResourceReaders.Testing.Inference;
public class ExtractExtensionFromPathsServiceTest
{
    [Test]
    [TestCase(@"C:\", "file.csv", "csv")]
    [TestCase(@"C:\foo\", "bar.csv", "csv")]
    [TestCase(@"C:\foo\", "bar.csv.gz", "csv.gz")]
    public void TryGetPathExtension_ShouldReturnTrue_WhenLocal(string root, string filename, string expected)
    {
        var extractor = new ExtractExtensionFromPathsService();
        var path = new Mock<IPath>();
<<<<<<< HEAD
        path.SetupGet(p => p.Value).Returns(filename);
=======
        path.SetupGet(p => p.RelativePath).Returns(filename);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        var paths = new[] { path.Object };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(extension, Is.EqualTo(expected));
        }
    }

    [Test]
    public void TryGetPathExtension_ShouldReturnTrue_WhenLocalMultiplePathsProvided()
    {
        var extractor = new ExtractExtensionFromPathsService();
        var paths = new[] {
<<<<<<< HEAD
            Mock.Of<IPath>(p => p.Value == "file-1.csv" && p.IsFullyQualified == false),
            Mock.Of<IPath>(p => p.Value == "file-2.csv" && p.IsFullyQualified == false)
=======
            Mock.Of<IPath>(p => p.RelativePath == "file-1.csv"),
            Mock.Of<IPath>(p => p.RelativePath == "file-2.csv")
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(extension, Is.EqualTo("csv"));
        }
    }

    [Test]
    public void TryGetPathExtension_ShouldReturnFalse_WhenLocalMultipleIncoherentPathsProvided()
    {
        var extractor = new ExtractExtensionFromPathsService();
        var paths = new[] {
<<<<<<< HEAD
            Mock.Of<IPath>(p => p.Value == "file-1.csv" && p.IsFullyQualified == false),
            Mock.Of<IPath>(p => p.Value == "http://www.foo.org/file-2.csv" && p.IsFullyQualified == true)
=======
            Mock.Of<IPath>(p => p.RelativePath == "file-1.csv"),
            Mock.Of<IPath>(p => p.RelativePath == "http://www.foo.org/file-2.csv")
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        Assert.That(result, Is.False);
    }

    [Test]
    [TestCase("http://www.foo.com/file.csv", "csv")]
    [TestCase("http://www.foo.com/foo/bar.csv", "csv")]
    [TestCase("http://www.foo.com/bar.csv.gz", "csv.gz")]
    public void TryGetPathExtension_ShouldReturnTrue_WhenHttpExtensionIsExtracted(string path, string expected)
    {
        var extractor = new ExtractExtensionFromPathsService();
<<<<<<< HEAD
        var paths = new[] { Mock.Of<IPath>(p => p.Value == path) };
=======
        var paths = new[] { Mock.Of<IPath>(p => p.RelativePath == path) };
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        var result = extractor.TryGetPathExtension(paths, out var extension);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(extension, Is.EqualTo(expected));
        }
    }

    [Test]
    public void TryGetPathExtension_ShouldReturnTrue_WhenHttpMultiplePathsProvided()
    {
        var extractor = new ExtractExtensionFromPathsService();
        var paths = new[] {
<<<<<<< HEAD
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-1.csv"),
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-2.csv")
=======
                    Mock.Of<IPath>(p => p.RelativePath == "http://www.foo.com/file-1.csv"),
                    Mock.Of<IPath>(p => p.RelativePath == "http://www.foo.com/file-2.csv")
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
                };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(extension, Is.EqualTo("csv"));
        }
    }

    [Test]
    public void TryGetPathExtension_ShouldReturnFalse_WhenHttpMultipleIncoherentPathsProvided()
    {
        var extractor = new ExtractExtensionFromPathsService();
        var paths = new[] {
<<<<<<< HEAD
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-1.csv"),
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-2.txt")
=======
                    Mock.Of<IPath>(p => p.RelativePath == "http://www.foo.com/file-1.csv"),
                    Mock.Of<IPath>(p => p.RelativePath == "http://www.foo.com/file-2.txt")
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
                };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        Assert.That(result, Is.False);
    }
}
