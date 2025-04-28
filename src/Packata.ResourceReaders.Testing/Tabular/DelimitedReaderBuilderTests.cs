using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Storage;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders.Tabular;
using PocketCsvReader;

namespace Packata.ResourceReaders.Testing.Tabular;
public class DelimitedReaderBuilderTests
{
    private static IPath GetPath(string content)
    {
        var path = new Mock<IPath>();
        path.Setup(x => x.ExistsAsync()).ReturnsAsync(true);
        path.Setup(x => x.OpenAsync()).ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        return path.Object;
    }

    [Test]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader()
    {
        var resource = new Resource
        {
            Paths = [GetPath("a;b;c\n1;2;3\n4;5;6")],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDelimitedDialect() { Delimiter = ';', LineTerminator = "\n" }
        };
        var builder = new DelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("1"));
            Assert.That(dataReader.GetValue(0), Is.EqualTo("1"));
            Assert.That(((CsvDataReader)dataReader).GetFieldValue<int>(0), Is.EqualTo(1));
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
    public void ToDataReader_ExistingLocalResourceMultiple_ReturnsIDataReader()
    {
        var resource = new Resource
        {
            Paths = [GetPath("a;b;c\n1;2;3\n4;5;6"), GetPath("a;b;c\n7;8;9\n10;11;12")],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDelimitedDialect() { Delimiter = ';', LineTerminator = "\n" }
        };
        var builder = new DelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.TypeOf<CsvBatchDataReader>());
        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("1"));
            Assert.That(dataReader.GetValue(0), Is.EqualTo("1"));
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
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("7"));
            Assert.That(dataReader["b"], Is.EqualTo("8"));
            Assert.That(dataReader["c"], Is.EqualTo("9"));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader["a"], Is.EqualTo("10"));
            Assert.That(dataReader["b"], Is.EqualTo("11"));
            Assert.That(dataReader["c"], Is.EqualTo("12"));
        }
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    [TestCase(FieldsMatching.Exact)]
    [TestCase(FieldsMatching.Equal)]
    public void ToDataReader_ExistingLocalResourceWithSchema_ReturnsIDataReader(FieldsMatching fieldMatch)
    {
        var resource = new Resource
        {
            Paths = [GetPath("a;b;c;d\n1e2;01-04-2025;0,12;1200.16\n-2 000;01-05-2025;7.010.120,12;1,200.16")],
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDelimitedDialect() { Delimiter = ';', LineTerminator = "\n" },
            Schema = new Schema()
            {
                FieldsMatch = fieldMatch,
                Fields = [
                new IntegerField() { Name = "a", Type = "integer", Format = "i32",  GroupChar=' '}
                , new DateField() { Name = "b", Type = "date", Format = "%m-%d-%Y" }
                , new NumberField() { Name = "c", Type = "number", GroupChar='.', DecimalChar=','}
                , new NumberField() { Name = "d", Type = "number"}
            ]
            }
        };
        var builder = new DelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader.GetValue(0), Is.EqualTo(100));
            Assert.That(dataReader.GetValue(1), Is.EqualTo(new DateOnly(2025, 1, 4)));
            Assert.That(dataReader.GetValue(2), Is.EqualTo(0.12m));
            Assert.That(dataReader.GetValue(3), Is.EqualTo(1200.16m));
        }
        Assert.That(dataReader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader.GetValue(0), Is.EqualTo(-2000));
            Assert.That(dataReader.GetValue(1), Is.EqualTo(new DateOnly(2025, 1, 5)));
            Assert.That(dataReader.GetValue(2), Is.EqualTo(7010120.12m));
            Assert.Throws<FormatException>(() => dataReader.GetDecimal(3));
        }
        Assert.That(dataReader.Read(), Is.False);
    }
}
