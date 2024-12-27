using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.PathHandling;
using Packata.Core.ResourceReading;
using Packata.Core.Testing.PathHandling;
using PocketCsvReader;
using PocketCsvReader.Configuration;
using RichardSzalay.MockHttp;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Packata.Core.Testing.ResourceReading;
public class TableDelimitedReaderBuilderTests
{
    private static IPath GetPath()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(new MemoryStream(Encoding.UTF8.GetBytes("a;b;c\n1;2;3\n4;5;6")));
        return new LocalPath(fileSystem.Object, "", "my-resource-path");
    }

    [Test]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader()
    {
        var resource = new Resource() { Paths = [GetPath()], Type = "table", Name = "my-resource" };
        resource.Dialect = new TableDialect() { Delimiter = ';', LineTerminator = "\n" };
        var builder = new TableDelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader["a"], Is.EqualTo("1"));
        Assert.That(dataReader["b"], Is.EqualTo("2"));
        Assert.That(dataReader["c"], Is.EqualTo("3"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader["a"], Is.EqualTo("4"));
        Assert.That(dataReader["b"], Is.EqualTo("5"));
        Assert.That(dataReader["c"], Is.EqualTo("6"));
        Assert.That(dataReader.Read(), Is.False);
    }
}
