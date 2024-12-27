using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.PathHandling;
using RichardSzalay.MockHttp;

namespace Packata.Core.Testing.PathHandling;
public class HttpPathTests
{
    [Test]
    public void ToStream_ExistingSTream_ReturnsIt()
    {
        var data = "foo;bar\\r\\n0;1";
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond("text/csv", data);

        var httpClient = mockHttp.ToHttpClient();
        var path = new HttpPath(httpClient, "http://example.com/data.csv");
        Assert.That(path.ToStream(), Is.EqualTo(new MemoryStream(Encoding.UTF8.GetBytes(data))));
    }

    [Test]
    public void ToStream_NotFound_ThrowsFileNotFound()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var path = new HttpPath(httpClient, "http://example.com/data.csv");
        Assert.Throws<FileNotFoundException>(() => path.ToStream());
    }

    [Test]
    public void Exists_ExistingStream_ReturnsTrue()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond("text/csv", "foo;bar\\r\\n0;1");

        var httpClient = mockHttp.ToHttpClient();
        var path = new HttpPath(httpClient, "http://example.com/data.csv");
        Assert.That(path.Exists(), Is.True);
    }

    [Test]
    public void Exists_NotFound_ReturnsFalse()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://example.com/data.csv")
                    .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var path = new HttpPath(httpClient, "http://example.com/data.csv");
        Assert.That(path.Exists(), Is.False);
    }
}
