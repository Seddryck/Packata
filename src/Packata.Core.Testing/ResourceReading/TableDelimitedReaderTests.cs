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
public class TableDelimitedReaderTests
{
    private static IEnumerable<IPath> GetPaths()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(new MemoryStream(Encoding.UTF8.GetBytes("a,b,c\r\n1,2,3\r\n4,5,6\r\n")));
        yield return new LocalPath(fileSystem.Object, "", "my-resource-path");

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond("text/csv", "a,b,c\r\n1,2,3\r\n4,5,6\r\n");
        yield return new HttpPath(mockHttp.ToHttpClient(), "http://example.com/data.csv");
    }


    [Test]
    [TestCaseSource(nameof(GetPaths))]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader(IPath path)
    {
        var resource = new Resource() { Paths = [path], Type = "table", Name = "my-resource" };
        var csvReader = new CsvReader(new CsvProfile(true));
        var reader = new TableDelimitedReader(csvReader);
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
