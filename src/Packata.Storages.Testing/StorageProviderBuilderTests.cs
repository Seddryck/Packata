using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Storage;

namespace Packata.Storages.Testing;

internal class StorageProviderBuilderTests
{
    [Test]
    public void Build_RegisterHttp_Success()
    {
        var storageProvider = new StorageProviderBuilder()
            .Register("http", b => b.UseHttp())
            .Build();

        Assert.That(storageProvider, Is.Not.Null);
        Assert.That(storageProvider, Is.TypeOf<StorageProvider>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(storageProvider.CanHandle("http://foo.org/bar.csv"), Is.True);
            Assert.That(storageProvider.CanHandle("file://C:/goo/bar.csv"), Is.False);
        }
    }

    [Test]
    public void Build_RegisterHttpHttps_Success()
    {
        var storageProvider = new StorageProviderBuilder()
            .Register(["http", "https"], b => b.UseHttp())
            .Build();

        Assert.That(storageProvider, Is.Not.Null);
        Assert.That(storageProvider, Is.TypeOf<StorageProvider>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(storageProvider.CanHandle("http://foo.org/bar.csv"), Is.True);
            Assert.That(storageProvider.CanHandle("https://www.foo.com/bar.csv"), Is.True);
            Assert.That(storageProvider.CanHandle("file://C:/goo/bar.csv"), Is.False);
        }
    }

    [Test]
    public void Build_RegisterTwice_Throws()
    {
        var builder = new StorageProviderBuilder()
            .Register("http", b => b.UseHttp())
            .Register("http", b => b.UseHttp());
        Assert.Throws<InvalidOperationException>(()=> builder.Build());
    }
}
