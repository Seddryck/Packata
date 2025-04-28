using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Packata.Storages.Testing;
[TestFixture]
internal class ZipDataPackageContainerTests
{
    private const string CSV_CONTENT = "foo;bar\r\n1;Hello\r\n2;World";
    private MemoryStream _zipStream;
    private readonly Uri _baseUri = new ("zip:///");

    [SetUp]
    public void SetUp()
    {
        // Build an in‐memory ZIP containing:
        //   - datapackage.json (empty)
        //   - foo.csv       (with CSV_CONTENT)
        //   - Data/bar.csv  (empty)
        _zipStream = new MemoryStream();
        using (var archive = new ZipArchive(_zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("datapackage.json");

            var foo = archive.CreateEntry("foo.csv");
            using (var es = foo.Open())
            using (var writer = new StreamWriter(es, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(CSV_CONTENT);
            }

            archive.CreateEntry("Data/bar.csv");
        }

        // Rewind before reading
        _zipStream.Position = 0;
    }

    [TearDown]
    public void TearDown()
    {
        _zipStream.Dispose();
    }

    [Test]
    public async Task ExistsAsync_Success()
    {
        using var container = new ZipDataPackageContainer(_baseUri, _zipStream);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await container.ExistsAsync("datapackage.json"), Is.True);
            Assert.That(await container.ExistsAsync("foo.csv"), Is.True);

            Assert.That(await container.ExistsAsync("unknown.csv"), Is.False);

            Assert.That(await container.ExistsAsync("Data/bar.csv"), Is.True);
            Assert.That(await container.ExistsAsync("Data\\bar.csv"), Is.True);
            Assert.That(await container.ExistsAsync("missing\\bar.csv"), Is.False);
        }
    }

    [Test]
    public async Task OpenAsync_Success()
    {
        using var container = new ZipDataPackageContainer(_baseUri, _zipStream);
        using var stream = await container.OpenAsync("foo.csv");

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.CanRead, Is.True);

        var buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer);
        Assert.That(bytesRead, Is.GreaterThan(0));

        var index = Encoding.UTF8.GetPreamble().Length;
        string text = Encoding.UTF8.GetString(buffer, index, bytesRead- index);
        Assert.That(text, Is.EqualTo(CSV_CONTENT));
    }
}
