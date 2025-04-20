using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.PathHandling;
using Packata.Core.Testing.PathHandling;
using Packata.ResourceReaders.Tabular;

namespace Packata.ResourceReaders.Testing.Tabular;
public class ParquetReaderBuilderTests
{
    private static LocalPath GetPath()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"{typeof(ResourceTests).Namespace}.Resources.iris.parquet")
            ?? throw new FileNotFoundException("Resource not found", $"{typeof(ResourceTests).Namespace}.Resources.iris.parquet");

        var fileStream = new MemoryStream();
        stream.CopyTo(fileStream);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(x => x.Exists("my-resource-path")).Returns(true);
        fileSystem.Setup(x => x.OpenRead("my-resource-path")).Returns(fileStream);
        return new LocalPath(fileSystem.Object, "", "my-resource-path");
    }

    [Test]
    public void ToDataReader_ExistingLocalResource_ReturnsIDataReader()
    {
        var resource = new Resource
        {
            Paths = [GetPath()],
            Type = "table",
            Name = "my-resource",
            Format = "parquet"
        };
        var builder = new ParquetReaderBuilder();
        builder.Configure(resource);
        var reader = builder.Build();

        using var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        Assert.That(dataReader.Read(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dataReader[0], Is.EqualTo(5.10));
            Assert.That(dataReader["Sepal.Length"], Is.EqualTo(5.10));
            Assert.That(dataReader.GetValue(0), Is.EqualTo(5.10));
            Assert.That(dataReader.GetDouble(0), Is.EqualTo(5.10));
            Assert.That(dataReader.GetName(0), Is.EqualTo("Sepal.Length"));
            Assert.That(dataReader.GetOrdinal("Sepal.Length"), Is.EqualTo(0));

            Assert.That(dataReader[4], Is.EqualTo("setosa"));
            Assert.That(dataReader["Species"], Is.EqualTo("setosa"));
            Assert.That(dataReader.GetValue(4), Is.EqualTo("setosa"));
            Assert.That(dataReader.GetString(4), Is.EqualTo("setosa"));
            Assert.That(dataReader.GetName(4), Is.EqualTo("Species"));
            Assert.That(dataReader.GetOrdinal("Species"), Is.EqualTo(4));
        }
    }
}
