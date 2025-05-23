﻿using System;
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
        path.SetupGet(p => p.Value).Returns(filename);
        path.SetupGet(p => p.IsFullyQualified).Returns(false);
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
            Mock.Of<IPath>(p => p.Value == "file-1.csv" && p.IsFullyQualified == false),
            Mock.Of<IPath>(p => p.Value == "file-2.csv" && p.IsFullyQualified == false)
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
            Mock.Of<IPath>(p => p.Value == "file-1.csv" && p.IsFullyQualified == false),
            Mock.Of<IPath>(p => p.Value == "http://www.foo.org/file-2.csv" && p.IsFullyQualified == true)
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
        var paths = new[] { Mock.Of<IPath>(p => p.Value == path && p.IsFullyQualified == true) };
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
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-1.csv" && p.IsFullyQualified == true),
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-2.csv" && p.IsFullyQualified == true)
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
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-1.csv" && p.IsFullyQualified == true),
                    Mock.Of<IPath>(p => p.Value == "http://www.foo.com/file-2.txt" && p.IsFullyQualified == true)
                };
        var result = extractor.TryGetPathExtension(paths, out var extension);
        Assert.That(result, Is.False);
    }
}
