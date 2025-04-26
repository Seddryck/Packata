using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Storage;
using RichardSzalay.MockHttp;

namespace Packata.ResourceReaders.Testing;
public class ResourceTests
{
    [Test]
    public void ToDataReader_SinglePropertySet_ReturnsDataReader()
    {
        var path = new Mock<IPath>();
        path.SetupGet(p => p.RelativePath).Returns("file.csv");
        path.Setup(p => p.ExistsAsync()).ReturnsAsync(true);
        path.Setup(p => p.OpenAsync()).ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("a,b,c\r\n1,2,3\r\n4,5,6\r\n")));

        var resource = new Resource() { Paths = [path.Object], Name = "my-resource", Type = "table" };
        var dataReader = resource.ToDataReader();

        Assert.That(dataReader, Is.Not.Null);
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
}
