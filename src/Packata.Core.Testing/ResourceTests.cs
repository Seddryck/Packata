using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.PathHandling;
using RichardSzalay.MockHttp;

namespace Packata.Core.Testing;
public class ResourceTests
{
    [Test]
    public void ToDataReader_SinglePropertySet_ReturnsDataReader()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond("text/csv", "a,b,c\r\n1,2,3\r\n4,5,6\r\n");
        var path = new HttpPath(mockHttp.ToHttpClient(), "http://example.com/data.csv");

        var resource = new Resource() { Paths = [path], Name = "my-resource", Type = "table" };
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
