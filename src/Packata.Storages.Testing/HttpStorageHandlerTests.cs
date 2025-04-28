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
internal class HttpStorageHandlerTests
{
    private const string CSV_CONTENT = "foo;bar\r\n1;Hello\r\n2;World";

    [Test]
    public async Task ExistsAsync_OnExisting_True()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Head, "https://example.com/base/foo.csv").Respond(HttpStatusCode.OK);

        using var httpClient = mockHttp.ToHttpClient();
        var handler = new HttpStorageHandler(httpClient);
        Assert.That(await handler.ExistsAsync("https://example.com/base/foo.csv"), Is.True);

        // verify all expectations were hit
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ExistsAsync_OnNotExisting_False()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Head, "https://example.com/base/foo.csv").Respond(HttpStatusCode.NotFound);

        using var httpClient = mockHttp.ToHttpClient();
        var handler = new HttpStorageHandler(httpClient);
        Assert.That(await handler.ExistsAsync("https://example.com/base/foo.csv"), Is.False);

        // verify all expectations were hit
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task OpenAsync_Existing_Success()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/base/foo.csv")
            .Respond("text/csv", CSV_CONTENT);

        using var httpClient = mockHttp.ToHttpClient();
        var handler = new HttpStorageHandler(httpClient);
        using var stream = await handler.OpenAsync("https://example.com/base/foo.csv");

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

    [Test]
    public void OpenAsync_NotExisting_Failure()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/base/foo.csv")
            .Respond(HttpStatusCode.NotFound);

        using var httpClient = mockHttp.ToHttpClient();
        var handler = new HttpStorageHandler(httpClient);
        Assert.ThrowsAsync<FileNotFoundException>(async () => await handler.OpenAsync("https://example.com/base/foo.csv"));
    }
}
