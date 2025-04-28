using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Stowage;
using Stowage.Impl;

namespace Packata.Storages.Testing;

internal class DataPackageLocatorBuilderTests
{
    [Test]
    public void Build_RegisterFileSystem_Success()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(locator.CanHandle(new Uri("file://C:/foo/bar/")), Is.True);
            Assert.That(locator.CanHandle(new Uri("http://www.foo.com/bar/")), Is.False);
        }
    }

    [Test]
    public void Build_RegisterHttpForManySchemes_Success()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register(["http", "https"], b => b.UseHttp(new HttpClient()))
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(locator.CanHandle(new Uri("https://www.foo.com/bar/")), Is.True);
            Assert.That(locator.CanHandle(new Uri("http://intranet.foo.net/bar/")), Is.True);
        }
    }

    [Test]
    public void Build_RegisterFileSystemAndHttp_Success()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Register("http", b => b.UseHttp(new HttpClient()))
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(locator.CanHandle(new Uri("http://intranet.foo.net/bar/")), Is.True);
            Assert.That(locator.CanHandle(new Uri("file://C:/foo/bar/")), Is.True);
        }
    }

    [Test]
    public void Build_RegisterAll_Success()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Register(["http", "https"], b => b.UseHttp(new HttpClient()))
            .Register("az", b => b.UseAzure("foo").WithSharedKey("bar"))
            .Register("aws", b => b.UseAws("foo", "bar", "qrz"))
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
    }

    [Test]
    public void Build_RegisterZipArchive_Success()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Register("zip", b => b.UseZipArchive())
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
        Assert.That(locator.CanHandle(new Uri("file://C:/foo/bar.zip")), Is.True);
    }

    [Test]
    public void Build_WithoutRegisterZipArchive_CannotHandle()
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Build();

        Assert.That(locator, Is.Not.Null);
        Assert.That(locator, Is.TypeOf<DataPackageLocator>());
        Assert.That(locator.CanHandle(new Uri("file://C:/foo/bar.zip")), Is.False);
    }
}
