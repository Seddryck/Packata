using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public sealed class ArchiveCommandTests
{
    private static readonly string[] Suffixes = ["zip", "tar", "tar.gz", "tar.br"];
    private const string Package = """
        {"name":"sample","resources":[{"name":"customers","path":"http://127.0.0.1:1/must-not-open.csv","format":"csv",
        "schema":{"fields":[{"name":"id","type":"integer"}]},"dialect":{"header":true}}]}
        """;

    public static IEnumerable<object[]> Commands()
    {
        foreach (var suffix in Suffixes)
        {
            foreach (var remote in new[] { false, true })
            {
                foreach (var command in new[] { "validate", "inspect", "diff", "compare", "schema-export", "dialect-export" })
            yield return [suffix, remote, command];
            }
        }
    }

    [TestCaseSource(nameof(Commands))]
    public async Task Metadata_commands_accept_all_archives_without_opening_resources(string suffix, bool remote, string command)
    {
        using var workspace = new CliWorkspace();
        var bytes = Archive(suffix, ("nested/datapackage.json", Package));
        var path = Path.Combine(workspace.Path, $"package.{suffix.ToUpperInvariant()}");
        await File.WriteAllBytesAsync(path, bytes);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task serve = Task.CompletedTask;
        var source = path;
        if (remote)
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            source = $"http://127.0.0.1:{port}/package.{suffix.ToUpperInvariant()}?download=1#ignored";
            serve = Serve(listener, bytes, command is "diff" or "compare" ? 2 : 1, timeout.Token);
        }

        var arguments = command switch
        {
            "schema-export" => new[] { "schema", "export", source, "--asset", "customers", "--output-format", "json-schema" },
            "dialect-export" => ["dialect", "export", source, "--endpoint", "customers-endpoint"],
            "diff" or "compare" => ["contract", command, source, source, "--output-format", "json"],
            _ => ["contract", command, source, "--output-format", "json"]
        };
        var result = await CliWorkspace.RunWithEnvironment(new Dictionary<string, string?>
        {
            ["PACKATA_CONFIG_HOME"] = workspace.Write("invalid-config", "not a directory"),
            ["DUBURL_PROVIDER_STORE"] = workspace.Write("invalid-providers", "not a directory")
        }, arguments);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        var json = JObject.Parse(result.Output);
        if (command == "validate") Assert.That((bool?)json["valid"], Is.True);
        if (command == "diff") Assert.That((bool?)json["hasChanges"], Is.False);
        if (command == "schema-export") AssertNullableIntegerSchema(json);
        if (command == "dialect-export") Assert.That((string?)json["delimiter"], Is.EqualTo(","));
        await serve;
        // A failed or successful command must release the local archive handle.
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void AssertNullableIntegerSchema(JObject schema)
    {
        Assert.That(schema["properties"]?["id"]?["type"]?.Values<string>(), Is.EqualTo(new[] { "integer", "null" }));
        Assert.That(schema["required"]?.Values<string>(), Does.Contain("id"));
    }

    [TestCase("zip")]
    [TestCase("tar")]
    [TestCase("tar.gz")]
    [TestCase("tar.br")]
    public async Task All_supported_descriptor_names_and_standards_are_accepted(string suffix)
    {
        using var workspace = new CliWorkspace();
        foreach (var (name, content) in new[]
        {
            ("datapackage.json", CliWorkspace.PackageJson),
            ("datapackage.yaml", CliWorkspace.PackageYaml),
            ("datapackage.yml", CliWorkspace.PackageYaml),
            ("contract.yaml", CliWorkspace.Odcs),
            ("contract.yml", CliWorkspace.Odcs)
        })
        {
            var path = Path.Combine(workspace.Path, $"package.{suffix}");
            await File.WriteAllBytesAsync(path, Archive(suffix, ($"nested/{name}", content)));
            var document = await ContractSource.LoadAsync(path, "auto", default);
            Assert.That(document.Map().IsSuccessful, Is.True, name);
        }
    }

    [TestCase("tar")]
    [TestCase("tar.gz")]
    [TestCase("tar.br")]
    public async Task Comparison_accepts_zip_baseline_and_tar_candidate(string suffix)
    {
        using var workspace = new CliWorkspace();
        var baseline = Path.Combine(workspace.Path, "baseline.zip");
        var candidate = Path.Combine(workspace.Path, $"candidate.{suffix}");
        await File.WriteAllBytesAsync(baseline, Archive("zip", ("datapackage.json", Package)));
        await File.WriteAllBytesAsync(candidate, Archive(suffix, ("nested/datapackage.json", Package)));
        foreach (var command in new[] { "diff", "compare" })
        {
            var result = await CliWorkspace.Run("contract", command, baseline, candidate, "--output-format", "json");
            Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        }
    }

    [TestCase("zip", false)]
    [TestCase("tar", false)]
    [TestCase("tar.gz", false)]
    [TestCase("tar.br", false)]
    [TestCase("zip", true)]
    [TestCase("tar", true)]
    [TestCase("tar.gz", true)]
    [TestCase("tar.br", true)]
    public async Task Missing_or_ambiguous_descriptors_are_rejected(string suffix, bool ambiguous)
    {
        using var workspace = new CliWorkspace();
        var entries = ambiguous
            ? new[] { ("datapackage.json", Package), ("nested/contract.yaml", CliWorkspace.Odcs) }
            : [("readme.txt", "no descriptor")];
        var path = Path.Combine(workspace.Path, $"package.{suffix}");
        await File.WriteAllBytesAsync(path, Archive(suffix, entries));
        var result = await CliWorkspace.Run("contract", "validate", path, "--output-format", "json");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output + result.Error);
        Assert.That((bool?)JObject.Parse(result.Output)["valid"], Is.False);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("zip")]
    [TestCase("tar")]
    [TestCase("tar.gz")]
    [TestCase("tar.br")]
    public async Task Invalid_archives_have_the_same_comparison_diagnostic(string suffix)
    {
        using var workspace = new CliWorkspace();
        var baseline = workspace.Write("baseline.json", Package);
        var invalid = workspace.Write($"invalid.{suffix}", "not an archive");
        foreach (var command in new[] { "diff", "compare" })
        {
            var result = await CliWorkspace.Run("contract", command, baseline, invalid, "--output-format", "json");
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output + result.Error);
            Assert.That(result.Output + result.Error, Does.Contain("CLI021"));
        }
    }

    [TestCase("zip")]
    [TestCase("tar")]
    [TestCase("tar.gz")]
    [TestCase("tar.br")]
    public async Task Inference_reports_missing_archives(string suffix)
    {
        using var workspace = new CliWorkspace();
        var local = Path.Combine(workspace.Path, $"missing.{suffix}");
        var inference = await CliWorkspace.Run("schema", "infer", local, "--asset", "customers");
        Assert.That(inference.ExitCode, Is.EqualTo(1));
        Assert.That(inference.Output + inference.Error, Does.Contain("not found"));
    }

    private static byte[] Archive(string suffix, params (string Name, string Content)[] entries)
    {
        using var archive = new MemoryStream();
        if (suffix == "zip")
        {
            using (var writer = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    using var text = new StreamWriter(writer.CreateEntry(name).Open());
                    text.Write(content);
                }
            }

            return archive.ToArray();
        }
        using (var writer = new TarWriter(archive, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
            }
        }

        if (suffix == "tar") return archive.ToArray();
        using var output = new MemoryStream();
        using (Stream encoder = suffix == "tar.gz"
            ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(archive.ToArray());
        return output.ToArray();
    }

    private static async Task Serve(TcpListener listener, byte[] body, int requests, CancellationToken cancellationToken)
    {
        for (var i = 0; i < requests; i++)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            Assert.That(await reader.ReadLineAsync(cancellationToken), Does.Contain("?download=1"));
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }
    }
}
