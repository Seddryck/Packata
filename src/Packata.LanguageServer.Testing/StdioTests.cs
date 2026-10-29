using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.LanguageServer.Testing;

[TestFixture]
public sealed class StdioTests
{
    [Test]
    public async Task DiskDiscovery_ReturnsVersionedPayloadsWithoutOpeningReferencedDataAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "packata-stdio-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "sales.yaml"), ContractServiceTests.Package);
            await using var client = await StdioClient.StartAsync();
            var folderUri = new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri;
            if (OperatingSystem.IsWindows()) folderUri = System.Text.RegularExpressions.Regex.Replace(folderUri, @"^(file:///[a-zA-Z]):/", "$1%3A/");
            var result = await client.CommandAsync("packata.workspace.discover", new { workspaceFolders = new[] { folderUri } });
            Assert.That(result["contracts"], Has.Count.EqualTo(1));
            Assert.That(result["contracts"]![0]!["version"], Is.Not.Null, "Disk version must be explicit JSON null.");
            Assert.That(result["contracts"]![0]!["version"]!.Type, Is.EqualTo(JTokenType.Null));
            await client.StopAsync();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Server_InitializesSynchronizesValidatesAndShutsDownAsync()
    {
        await using var client = await StdioClient.StartAsync();
        Assert.That(client.Commands, Does.Contain("packata.contract.inspect"));
        Assert.That(client.Commands, Does.Contain("packata.contract.validate"));
        Assert.That(client.Commands, Does.Contain("packata.workspace.discover"));
        const string uri = "file:///workspace/contract.yaml";
        await client.NotifyAsync("textDocument/didOpen", new { textDocument = new { uri, languageId = "yaml", version = 1, text = ContractServiceTests.Odcs } });
        var first = await client.CommandAsync("packata.contract.inspect", new { uri, schemaVersion = 1 });
        Assert.That((int)first["version"]!, Is.EqualTo(1));
        Assert.That((string?)first["contract"]?["identity"]?["name"], Is.EqualTo("Customers"));
        await client.NotifyAsync("textDocument/didChange", new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = ContractServiceTests.Odcs.Replace("Customers", "Unsaved") } } });
        var changed = await client.CommandAsync("packata.contract.inspect", new { uri });
        Assert.That((int)changed["version"]!, Is.EqualTo(2));
        Assert.That((string?)changed["contract"]?["identity"]?["name"], Is.EqualTo("Unsaved"));
        var validation = await client.CommandAsync("packata.contract.validate", new { uri });
        Assert.That((bool)validation["valid"]!, Is.True);
        await client.NotifyAsync("textDocument/didChange", new { textDocument = new { uri, version = 3 }, contentChanges = new[] { new { text = "kind: DataContract\nschema: [" } } });
        var invalid = await client.CommandAsync("packata.contract.validate", new { uri });
        Assert.That((bool)invalid["valid"]!, Is.False);
        Assert.That((string?)invalid["diagnostics"]?[0]?["code"], Is.EqualTo("PKT001"));
        // Validation publishes the same diagnostics through LSP, not a separate client collection.
        Assert.That(client.Notifications.Any(value => (string?)value["method"] == "textDocument/publishDiagnostics"
            && (int?)value["params"]?["version"] == 3), Is.True);
        await client.NotifyAsync("textDocument/didClose", new { textDocument = new { uri } });
        await client.StopAsync();
    }

    [Test]
    public async Task Restart_ResynchronizesUnsavedBuffersAndRejectsUnsupportedPayloadsAsync()
    {
        const string uri = "file:///workspace/datapackage.json";
        foreach (var iteration in Enumerable.Range(0, 2))
        {
            await using var client = await StdioClient.StartAsync();
            await client.NotifyAsync("textDocument/didOpen", new { textDocument = new { uri, languageId = "yaml", version = 5, text = ContractServiceTests.Package } });
            var result = await client.CommandAsync("packata.contract.inspect", new { uri });
            Assert.That((int)result["version"]!, Is.EqualTo(5));
            var error = await client.RequestAsync("workspace/executeCommand", new { command = "packata.contract.inspect", arguments = new[] { new { uri, schemaVersion = 99 } } });
            Assert.That(error["error"], Is.Not.Null);
            Assert.That((int?)error["error"]?["code"], Is.EqualTo(-32602));
            Assert.That(error.ToString(), Does.Not.Contain("System.NotSupportedException"));
            var id = await client.SendRequestAsync("workspace/executeCommand", new { command = "packata.workspace.discover", arguments = new[] { new { workspaceFolders = Array.Empty<string>(), schemaVersion = 1 } } });
            await client.NotifyAsync("$/cancelRequest", new { id });
            await client.ResponseAsync(id); // A completed response can race cancellation; the next operation must still succeed.
            Assert.That((int)(await client.CommandAsync("packata.contract.inspect", new { uri }))["schemaVersion"]!, Is.EqualTo(1));
            await client.StopAsync();
        }
    }
}

internal sealed class StdioClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    private readonly Task<string> stderr;
    private int nextId;
    public string[] Commands { get; private set; } = [];
    public List<JObject> Notifications { get; } = [];
    private StdioClient(Process process)
    {
        this.process = process;
        stderr = process.StandardError.ReadToEndAsync(timeout.Token);
    }
    public static async Task<StdioClient> StartAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        var client = new StdioClient(Process.Start(start)!);
        try
        {
            var response = await client.RequestAsync("initialize", new
            {
                processId = (int?)null, rootUri = (string?)null,
                capabilities = new { workspace = new { executeCommand = new { dynamicRegistration = false } }, textDocument = new { synchronization = new { dynamicRegistration = false } } }
            });
            Assert.That(response["error"], Is.Null, response.ToString());
            Assert.That(response["result"]?["capabilities"]?["executeCommandProvider"], Is.Not.Null, response.ToString());
            client.Commands = response["result"]!["capabilities"]!["executeCommandProvider"]!["commands"]!.Values<string>().Cast<string>().ToArray();
            await client.NotifyAsync("initialized", new { });
            return client;
        }
        catch { await client.DisposeAsync(); throw; }
    }
    public async Task<JToken> CommandAsync(string command, object argument)
    {
        var args = JObject.FromObject(argument);
        args["schemaVersion"] ??= 1;
        var response = await RequestAsync("workspace/executeCommand", new { command, arguments = new[] { args } });
        Assert.That(response["error"], Is.Null, response.ToString());
        return response["result"]!;
    }
    public async Task<JObject> RequestAsync(string method, object? args)
        => await ResponseAsync(await SendRequestAsync(method, args));
    public async Task<int> SendRequestAsync(string method, object? args)
    {
        var id = ++nextId;
        await SendAsync(new { jsonrpc = "2.0", id, method, @params = args });
        return id;
    }
    public Task NotifyAsync(string method, object? args) => SendAsync(new { jsonrpc = "2.0", method, @params = args });
    private async Task SendAsync(object value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
        var input = process.StandardInput.BaseStream;
        await input.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), timeout.Token);
        await input.WriteAsync(bytes, timeout.Token);
        await input.FlushAsync(timeout.Token);
    }
    public async Task<JObject> ResponseAsync(int id)
    {
        var output = process.StandardOutput.BaseStream;
        while (true)
        {
            var header = new StringBuilder();
            var next = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                await output.ReadExactlyAsync(next, timeout.Token);
                header.Append((char)next[0]);
            }
            var length = int.Parse(header.ToString().Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
            var body = new byte[length];
            await output.ReadExactlyAsync(body, timeout.Token);
            var message = JObject.Parse(Encoding.UTF8.GetString(body));
            if ((int?)message["id"] == id) return message;
            Notifications.Add(message);
        }
    }
    public async Task StopAsync()
    {
        await RequestAsync("shutdown", null);
        await NotifyAsync("exit", null);
        await process.WaitForExitAsync(timeout.Token);
        // This background drain deliberately starts with the process, before protocol requests,
        // so a full stderr pipe cannot deadlock the server.
#pragma warning disable VSTHRD003
        var errors = await stderr;
#pragma warning restore VSTHRD003
        Assert.That(process.ExitCode, Is.Zero, errors);
    }
    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process.Dispose(); timeout.Dispose();
    }
}
