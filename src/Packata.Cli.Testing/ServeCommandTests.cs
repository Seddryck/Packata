using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using Packata.Http;

namespace Packata.Cli.Testing;

public class ServeCommandTests
{
    [TestCase("--help")]
    [TestCase("--port", "-1")]
    [TestCase("--address", "invalid")]
    [TestCase("--formats", "invalid")]
    [TestCase("--batch-size", "0")]
    public async Task ServeSettingsExplainAndRejectInvalidInvocation(params string[] options)
    {
        var arguments = new[] { "data", "serve", "missing.json", "--asset", "customers", "--binding", "0" }.Concat(options).ToArray();
        var result = await CliWorkspace.Run(arguments);
        if (options[0] == "--help") Assert.That(result.Output, Does.Contain("--binding").And.Contain("--formats").And.Contain("--port"));
        else Assert.That(result.ExitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task ServeRequiresExplicitAssetsAndBinding()
    {
        Assert.That((await CliWorkspace.Run("data", "serve", "missing.json")).ExitCode, Is.EqualTo(2));
        Assert.That((await CliWorkspace.Run("data", "serve", "missing.json", "--asset", "customers")).ExitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task SelectionFailureProducesNoReadiness()
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("contract.json", """
            {"name":"sample","resources":[{"name":"customers","path":"missing.csv","schema":{"fields":[{"name":"id","type":"integer"}]}}]}
            """);
        var result = await CliWorkspace.Run("data", "serve", path, "--asset", "missing", "--binding", "0");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("CLI140"));
    }

    [Test]
    public async Task CsvServiceEmitsMachineReadableReadinessAndNegotiatesSameAsset()
    {
        using var workspace = new CliWorkspace();
        workspace.Write("customers.csv", "id,name\n1,Alice\n2,Bob\n");
        var path = workspace.Write("contract.json", """
            {"name":"sample","resources":[{"name":"customers","path":"customers.csv","format":"csv","schema":{"fields":[{"name":"id","type":"integer"},{"name":"name","type":"string"}]}}]}
            """);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in new[] { "data", "serve", path, "--asset", "customers", "--binding", "0", "--port", "0", "--batch-size", "1" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.That(line, Is.Not.Null, process.HasExited ? await errors : "No readiness received.");
            using var readiness = JsonDocument.Parse(line!);
            Assert.That(readiness.RootElement.GetProperty("event").GetString(), Is.EqualTo("ready"));
            var baseUrl = readiness.RootElement.GetProperty("baseUrl").GetString();
            var asset = readiness.RootElement.GetProperty("assets")[0];
            Assert.That(asset.GetProperty("assetId").GetString(), Is.EqualTo("customers"));
            using var http = new HttpClient { BaseAddress = new(baseUrl!), Timeout = TimeSpan.FromSeconds(10) };
            foreach (var media in new[] { "application/x-ndjson", "application/avro", "application/vnd.apache.arrow.stream" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, asset.GetProperty("dataUrl").GetString());
                request.Headers.Accept.ParseAdd(media);
                using var response = await http.SendAsync(request);
                Assert.That((int)response.StatusCode, Is.EqualTo(200), await response.Content.ReadAsStringAsync());
                Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo(media));
                Assert.That((await http.GetFromJsonAsync<TransferStatus>(response.Headers.GetValues("Packata-Completion").Single()))!.State,
                    Is.EqualTo("complete"));
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
