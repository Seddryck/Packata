﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core.PathHandling;
using Packata.Core.ResourceReading;
using Packata.Core.Testing.PathHandling;

namespace Packata.Core.Testing.ResourceReading;
public class TableDelimitedReaderBuilderTests
{
    private static IPath GetPath(string content)
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        return new LocalPath(fileSystem.Object, "", "my-resource-path");
    }

    [Test]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader()
    {
        var resource = new Resource() { Paths = [GetPath("a;b;c\n1;2;3\n4;5;6")], Type = "table", Name = "my-resource" };
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

    [Test]
    [TestCase(FieldsMatching.Exact)]
    [TestCase(FieldsMatching.Equal)]
    public void ToDataReader_ExistingLocalResourceWithSchema_ReturnsIDataReader(FieldsMatching fieldMatch)
    {
        var resource = new Resource() { Paths = [GetPath("a;b;c\n1;01-04-2025\n2;01-05-2025")], Type = "table", Name = "my-resource" };
        resource.Dialect = new TableDialect() { Delimiter = ';', LineTerminator = "\n" };
        resource.Schema = new Schema()
        {
            FieldsMatch = fieldMatch,
            Fields = [
                new Field() { Name = "a", Type = "integer", Format = "i32" }
                , new Field() { Name = "b", Type = "date", Format = "%m-%d-%Y" }
            ]
        };
        var builder = new TableDelimitedReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValue(0), Is.EqualTo(1));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(new DateOnly(2025, 1, 4)));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValue(0), Is.EqualTo(2));
        Assert.That(dataReader.GetValue(1), Is.EqualTo(new DateOnly(2025, 1, 5)));
        Assert.That(dataReader.Read(), Is.False);
    }
}
