using System.Text;
using System.Text.Json;
using NUnit.Framework;
using static Packata.Cli.Testing.RemoteInferenceTests;

namespace Packata.Cli.Testing;

public sealed class DataPullCommandTests
{
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task Pull_DownloadsAllResourcePathsAsServed(string format)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        var descriptor = format == "json"
            ? JsonDescriptor(["./nested//one.bin?version=1", "nested/second%20file.bin"], [server.Url + "other/three.bin?version=2"])
            : $"""
              resources:
                - name: first
                  path: ['./nested//one.bin?version=1', 'nested/second%20file.bin']
                - name: second
                  path: '{server.Url}other/three.bin?version=2'
              """;
        var descriptorPath = $"/dataset/datapackage.{format}";
        server.Routes["/start"] = (302, Encoding.ASCII.GetBytes(server.Url.TrimEnd('/') + descriptorPath));
        server.Routes[descriptorPath] = (200, Encoding.UTF8.GetBytes(descriptor));
        server.Routes["/dataset/nested//one.bin?version=1"] = (200, [0, 255, 10]);
        server.Routes["/dataset/nested/second%20file.bin"] = (200, [13, 10, 0]);
        server.Routes["/other/three.bin?version=2"] = (200, [42]);

        var result = await workspace.RunInWorkspace("data", "pull", server.Url + "start");

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(File.ReadAllBytes(Path.Combine(workspace.Path, "nested", "one.bin")), Is.EqualTo(new byte[] { 0, 255, 10 }));
        Assert.That(File.ReadAllBytes(Path.Combine(workspace.Path, "nested", "second file.bin")), Is.EqualTo(new byte[] { 13, 10, 0 }));
        Assert.That(File.ReadAllBytes(Path.Combine(workspace.Path, "three.bin")), Is.EqualTo(new byte[] { 42 }));
    }

    [TestCase("download/file.bin", "existing")]
    [TestCase("download/file.bin", "duplicate")]
    [TestCase("download/file.bin", "directory")]
    [TestCase("download/file.bin", "parent")]
    public async Task Pull_ReportsDestinationConflictsBeforeDownloading(string path, string conflict)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        var target = Path.Combine(workspace.Path, "output");
        string? existing = null;
        if (conflict == "existing") existing = workspace.Write("output/" + path, "original");
        if (conflict == "directory") Directory.CreateDirectory(Path.Combine(target, path));
        var other = conflict switch
        {
            "duplicate" => "./download/%66ile.bin",
            "parent" => "download",
            _ => "later.bin"
        };
        server.Routes["/datapackage.json"] = (200, Encoding.UTF8.GetBytes(JsonDescriptor([path], [other])));
        server.Routes["/" + path] = (500, []);

        var result = await CliWorkspace.Run("data", "pull", server.Url + "datapackage.json", "--output-dir", target);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("Destination conflict").And.Contain("download"));
        if (existing is not null) Assert.That(File.ReadAllText(existing), Is.EqualTo("original"));
        Assert.That(File.Exists(Path.Combine(target, "later.bin")), Is.False);
    }

    [TestCase("../escape.bin")]
    [TestCase("safe/%2e%2e/escape.bin")]
    [TestCase("safe%2Fescape.bin")]
    [TestCase(@"\escape.bin")]
    [TestCase("/escape.bin")]
    [TestCase("ftp://example.org/escape.bin")]
    public async Task Pull_RejectsUnsafeResourcePaths(string path)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        server.Routes["/datapackage.json"] = (200, Encoding.UTF8.GetBytes(JsonDescriptor([path])));
        var destination = Path.Combine(workspace.Path, "output");

        var result = await CliWorkspace.Run("data", "pull", server.Url + "datapackage.json", "--output-dir", destination);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("$.resources[0]"));
        Assert.That(Directory.GetFileSystemEntries(workspace.Path), Is.Empty);
    }

    [Test]
    public async Task Pull_RejectsLinkedDestinationDirectories()
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        var destination = Directory.CreateDirectory(Path.Combine(workspace.Path, "output"));
        var outside = Directory.CreateDirectory(Path.Combine(workspace.Path, "outside"));
        try { Directory.CreateSymbolicLink(Path.Combine(destination.FullName, "linked"), outside.FullName); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            Assert.Ignore("Creating symbolic links requires additional privileges on this Windows host.");
        }
        server.Routes["/datapackage.json"] = (200, Encoding.UTF8.GetBytes(JsonDescriptor(["linked/file.bin"])));

        var result = await CliWorkspace.Run("data", "pull", server.Url + "datapackage.json", "--output-dir", destination.FullName);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain("Destination conflict"));
        Assert.That(outside.GetFileSystemInfos(), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Pull_ReportsFailedResourceAndRemovesIncompleteFile(bool truncated)
    {
        using var workspace = new CliWorkspace();
        await using var server = new HttpFixture();
        var destination = Path.Combine(workspace.Path, "output");
        var path = "/nested/broken.bin?sig=private-signature";
        server.Routes["/datapackage.json"] = (200, Encoding.UTF8.GetBytes(JsonDescriptor(["good.bin"], [path[1..]])));
        server.Routes["/good.bin"] = (200, [42]);
        server.Routes[path] = truncated ? (200, [1, 2]) : (404, []);
        if (truncated) server.DeclaredLengths[path] = 10;

        var result = await CliWorkspace.Run("data", "pull", server.Url + "datapackage.json", "--output-dir", destination);

        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Io));
        Assert.That(result.Error, Does.Contain("broken.bin").And.Contain("$.resources[1]").And.Not.Contain("private-signature"));
        Assert.That(Directory.GetFiles(destination, "*", SearchOption.AllDirectories), Is.EqualTo(new[] { Path.Combine(destination, "good.bin") }));
        Assert.That(File.ReadAllBytes(Path.Combine(destination, "good.bin")), Is.EqualTo(new byte[] { 42 }));
    }

    [Test]
    public async Task Pull_RejectsLocalDescriptor()
    {
        using var workspace = new CliWorkspace();
        var descriptor = workspace.Write("datapackage.json", CliWorkspace.PackageJson);

        var result = await workspace.RunInWorkspace("data", "pull", descriptor);

        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invocation));
        Assert.That(result.Error, Does.Contain("HTTP(S)"));
    }

    private static string JsonDescriptor(params string[][] paths) =>
        JsonSerializer.Serialize(new { resources = paths.Select(path => new { path }) });
}
