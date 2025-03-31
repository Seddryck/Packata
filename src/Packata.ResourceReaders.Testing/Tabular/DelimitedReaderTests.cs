using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chrononuensis;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.PathHandling;
using Packata.Core.Testing.PathHandling;
using Packata.ResourceReaders.Tabular;
using PocketCsvReader;
using PocketCsvReader.Configuration;
using RichardSzalay.MockHttp;

namespace Packata.ResourceReaders.Testing.Tabular;
public class DelimitedReaderTests
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
        var csvReader = new CsvReaderBuilder().WithDialect(d => d.WithHeader()).Build();
        var reader = new DelimitedReader(csvReader);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader, Is.InstanceOf<CsvDataReader>());
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("1"));
            Assert.That(dataReader["b"], Is.EqualTo("2"));
            Assert.That(dataReader["c"], Is.EqualTo("3"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("4"));
            Assert.That(dataReader["b"], Is.EqualTo("5"));
            Assert.That(dataReader["c"], Is.EqualTo("6"));
        }
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_YearYearMonthDecimal_ReturnsIDataReader()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(new MemoryStream(Encoding.UTF8.GetBytes("a;b;c;d\r\n2025;2025-01;107,25;10")));

        var resource = new Resource
        {
            Paths = [new LocalPath(fileSystem.Object, "", "my-resource-path")],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDelimitedDialect() { Delimiter = ';', LineTerminator = "\r\n", Header = true },
            Schema = new Schema()
            {
                Fields = [
                    new YearField() { Name = "a", Type = "year" },
                    new YearMonthField() { Name = "b", Type = "yearmonth" },
                    new NumberField() { Name = "c", Type = "number", DecimalChar = ',' },
                    new IntegerField() { Name = "d", Type = "integer", Format="i16" }
                ]
            }
        };
        var builder = new DelimitedReaderBuilder();
        builder.Register("year", typeof(int));
        builder.Register("yearmonth", typeof(YearMonth));
        builder.Configure(resource);
        var reader = builder.Build();
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader, Is.InstanceOf<CsvDataReader>());
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo(2025));
            Assert.That(dataReader["b"], Is.EqualTo(new YearMonth(2025, 1)));
            Assert.That(dataReader["c"], Is.EqualTo(107.25m));
            Assert.That(dataReader["d"], Is.TypeOf<short>());
            Assert.That(dataReader["d"], Is.EqualTo((short)10));
        }
        Assert.That(dataReader.Read(), Is.False);
    }
    
    [Test]
    public void ToDataReader_MultiFile_ReturnsIDataReader()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path-1")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path-1")).Returns(new MemoryStream(Encoding.UTF8.GetBytes("a;b\r\n1;foo")));
        fileSystem.Setup(x => x.Exists("my-resource-path-2")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path-2")).Returns(new MemoryStream(Encoding.UTF8.GetBytes("2;bar")));

        var resource = new Resource
        {
            Paths = [new LocalPath(fileSystem.Object, "", "my-resource-path-1"), new LocalPath(fileSystem.Object, "", "my-resource-path-2")],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDelimitedDialect() { Delimiter = ';', LineTerminator = "\r\n", Header = true, HeaderRepeat = false },
            Schema = new Schema()
            {
                Fields = [
                    new IntegerField() { Name = "a", Type = "integer", Format="i16" },
                    new StringField() { Name = "b", Type = "string" }
                ]
            }
        };
        var builder = new DelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader, Is.InstanceOf<CsvBatchDataReader>());
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo(1));
            Assert.That(dataReader["b"], Is.EqualTo("foo"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo(2));
            Assert.That(dataReader["b"], Is.EqualTo("bar"));
        }
        Assert.That(dataReader.Read(), Is.False);
    }
}
