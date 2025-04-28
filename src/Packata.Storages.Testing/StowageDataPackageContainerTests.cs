using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Stowage;

namespace Packata.Storages.Testing;
internal class StowageDataPackageContainerTests
{
    private string? _directory;
    private const string CSV_CONTENT = @"foo;bar\r\n1;Hello\r\n2;World";


    [OneTimeSetUp]
    public void Setup()
    {
        _directory = Guid.NewGuid().ToString() + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_directory);
        File.Create(Path.Combine(_directory, "datapackage.json")).Close();
        using (var csv = File.Create(Path.Combine(_directory, "foo.csv")))
        {
            csv.Write(Encoding.UTF8.GetBytes(CSV_CONTENT));
        }
        Directory.CreateDirectory(Path.Combine(_directory, "Data"));
        File.Create(Path.Combine(_directory, "Data", "bar.csv")).Close();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (!string.IsNullOrEmpty(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public async Task ExistsAsync_Success()
    {
        var uri = new Uri("file:///" + _directory);
        var root = Path.Combine(Environment.CurrentDirectory, _directory!);
        using var container = new StowageDataPackageContainer(uri, Files.Of.LocalDisk(root));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await container.ExistsAsync("datapackage.json"), Is.True);
            Assert.That(await container.ExistsAsync("foo.csv"), Is.True);
            Assert.That(await container.ExistsAsync("unknown.csv"), Is.False);
            Assert.That(await container.ExistsAsync(Path.Combine("Data", "bar.csv")), Is.True);
            Assert.That(await container.ExistsAsync(Path.Combine("unknown", "bar.csv")), Is.False);
        }
    }

    [Test]
    public async Task OpenAsync_Success()
    {
        var uri = new Uri("file:///" + _directory);
        var root = Path.Combine(Environment.CurrentDirectory, _directory!);
        using var container = new StowageDataPackageContainer(uri, Files.Of.LocalDisk(root));
        using var stream = await container.OpenAsync("foo.csv");
        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.CanRead, Is.True);
        var memory = new Memory<byte>(new byte[100]);
        int bytesRead = await stream.ReadAsync(memory);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytesRead, Is.GreaterThan(0));
            Assert.That(stream.Position, Is.GreaterThan(0));
            Assert.That(Encoding.UTF8.GetString(memory.Span.Slice(0, bytesRead)), Is.EqualTo(CSV_CONTENT));
        }
    }
}
