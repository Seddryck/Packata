using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using RichardSzalay.MockHttp;

namespace Packata.Storages.Testing;

[TestFixture]
internal class HttpDataPackageContainerTests
{
    private const string CSV_CONTENT = "foo;bar\r\n1;Hello\r\n2;World";
    private readonly Uri _baseUri = new ("https://example.com/base/");

    [Test]
    public async Task ExistsAsync_Success()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp!.When(HttpMethod.Head, _baseUri + "datapackage.json").Respond(HttpStatusCode.OK);
        mockHttp.When(HttpMethod.Head, _baseUri + "foo.csv").Respond(HttpStatusCode.OK);
        mockHttp.When(HttpMethod.Head, _baseUri + "unknown.csv").Respond(HttpStatusCode.NotFound);
        mockHttp.When(HttpMethod.Head, _baseUri + "Data/bar.csv").Respond(HttpStatusCode.OK);
        mockHttp.When(HttpMethod.Head, _baseUri + "unknown/bar.csv").Respond(HttpStatusCode.NotFound);

        using var httpClient = mockHttp.ToHttpClient();
        var container = new HttpDataPackageContainer(_baseUri, httpClient);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await container.ExistsAsync("datapackage.json"), Is.True);
            Assert.That(await container.ExistsAsync("foo.csv"), Is.True);
            Assert.That(await container.ExistsAsync("unknown.csv"), Is.False);

            // also support bakslashes in the relative path
            Assert.That(await container.ExistsAsync("Data\\bar.csv"), Is.True);
            Assert.That(await container.ExistsAsync("unknown\\bar.csv"), Is.False);
        }

        // verify all expectations were hit
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task OpenAsync_Success()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, _baseUri + "foo.csv")
            .Respond("text/csv", CSV_CONTENT);

        using var httpClient = mockHttp.ToHttpClient();
        
        using var container = new HttpDataPackageContainer(_baseUri, httpClient);
        using var stream = await container.OpenAsync("foo.csv");

        // Assert
        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.CanRead, Is.True);

        var buffer = new byte[100];
        var memory = new Memory<byte>(buffer);
        int bytesRead = await stream.ReadAsync(memory);
        Assert.That(bytesRead, Is.GreaterThan(0), "should have read at least 1 byte");

        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Assert.That(text, Is.EqualTo(CSV_CONTENT));

        // ensure no stray expectations
        mockHttp.VerifyNoOutstandingExpectation();
    }
}
