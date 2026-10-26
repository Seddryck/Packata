using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders;

namespace Packata.Cli.Testing;

[TestFixture("json")]
[TestFixture("yaml")]
[TestFixture("odcs")]
public sealed class StagedPreviewTests(string standard)
{
    private string Name => standard == "json" ? "datapackage.json" : "contract.yaml";
    private string Descriptor(string path, string format) => (standard switch
    {
        "odcs" => CliWorkspace.Odcs,
        "yaml" => CliWorkspace.PackageYaml,
        _ => CliWorkspace.PackageJson
    }).Replace("customers.csv", path).Replace("csv", format);
    private static byte[] Data(string format) => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "Resources", format == "xlsx" ? "my-book.xlsx" : "iris.parquet"));

    [TestCase("xlsx", false)]
    [TestCase("xlsx", true)]
    [TestCase("parquet", false)]
    [TestCase("parquet", true)]
    public async Task Preview_StagesHttpDataAndPreservesSource(string format, bool knownLength)
    {
        using var workspace = new CliWorkspace();
        await using var server = new RemoteInferenceTests.HttpFixture();
        const string route = "/download?sig=private-signature";
        var data = Data(format);
        server.Routes[route] = (200, data);
        if (knownLength) server.DeclaredLengths[route] = data.Length;
        var descriptor = Descriptor(server.Url + route.TrimStart('/'), format);
        var source = workspace.Write(Name, descriptor);
        var before = TemporaryFiles();
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--limit", "1", "--output-format", "json");
        AssertPreview(result);
        Assert.That(File.ReadAllText(source), Is.EqualTo(descriptor));
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    [TestCase("zip", "xlsx")]
    [TestCase("tar", "xlsx")]
    [TestCase("tar.gz", "xlsx")]
    [TestCase("tar.br", "xlsx")]
    [TestCase("zip", "parquet")]
    [TestCase("tar", "parquet")]
    [TestCase("tar.gz", "parquet")]
    [TestCase("tar.br", "parquet")]
    public async Task Preview_ReadsNestedLocalAndHttpArchives(string suffix, string format)
    {
        using var workspace = new CliWorkspace();
        await using var server = new RemoteInferenceTests.HttpFixture();
        var bytes = RemoteInferenceTests.Archive(suffix,
            ("nested/" + Name, Encoding.UTF8.GetBytes(Descriptor("../data/rows." + format, format))),
            ("data/rows." + format, Data(format)));
        var path = Path.Combine(workspace.Path, "dataset." + suffix);
        File.WriteAllBytes(path, bytes);
        server.Routes["/dataset." + suffix] = (200, bytes);
        foreach (var source in new[] { path, server.Url + "dataset." + suffix })
        {
            var before = TemporaryFiles();
            var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--limit", "1", "--output-format", "json");
            AssertPreview(result);
            Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
        }
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestCase("xlsx")]
    [TestCase("parquet")]
    public async Task PreviewAndInference_StagingLimitHasIndependentDiagnostic(string format)
    {
        using var workspace = new CliWorkspace();
        await using var server = new RemoteInferenceTests.HttpFixture();
        server.Routes["/data"] = (200, Data(format));
        server.DeclaredLengths["/data"] = Data(format).Length;
        var source = workspace.Write(Name, Descriptor(server.Url + "data", format));
        foreach (var command in new[] { new[] { "data", "preview" }, new[] { "schema", "infer" } })
        {
            var output = Path.Combine(workspace.Path, "output");
            var result = await CliWorkspace.Run([.. command, source, "--asset", "customers", "--staging-maximum-bytes", "4", "--output", output]);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output + result.Error, Does.Contain("staging byte limit").And.Not.Contain("MaximumFooterBytes").And.Not.Contain("MaximumBytes"));
            Assert.That(File.Exists(output), Is.False);
        }
    }

    [TestCase("parser")]
    [TestCase("transfer")]
    public async Task Preview_FailuresLeaveNoOutputOrStagingFiles(string failure)
    {
        using var workspace = new CliWorkspace();
        await using var server = new RemoteInferenceTests.HttpFixture();
        server.Routes["/data?sig=private-signature"] = (200, "invalid workbook"u8.ToArray());
        if (failure == "transfer") server.DeclaredLengths["/data?sig=private-signature"] = 1000;
        var source = workspace.Write(Name, Descriptor(server.Url + "data?sig=private-signature", "xlsx"));
        var output = Path.Combine(workspace.Path, "output");
        var before = TemporaryFiles();
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "customers", "--output", output);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output + result.Error, Does.Not.Contain("packata-stage-").And.Not.Contain("private-signature").And.Not.Contain("invalid workbook"));
        Assert.That(File.Exists(output), Is.False);
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    [Test]
    public async Task Preview_CancellationDuringHttpStagingReleasesTemporaryFiles()
    {
        await using var server = new RemoteInferenceTests.HttpFixture();
        server.StallPath = "/pending";
        using var cancellation = new CancellationTokenSource();
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation([server.Url + "pending"]), new DataFormat("parquet"));
        var factory = PreviewService.CreateFactory(new Uri(server.Url));
        var before = TemporaryFiles();
        var operation = factory.OpenAsync(endpoint, cancellationToken: cancellation.Token).AsTask();
        await server.HeadersSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThatAsync(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(TemporaryFiles(), Is.EquivalentTo(before));
    }

    [Test]
    public async Task Preview_CsvRemainsNonSeekableAndIgnoresStagingLimit()
    {
        var upstream = new SequentialInput("id,name\n1,Alice\n"u8.ToArray());
        var streams = new OneStream(upstream);
        var resolver = new ResourceAcquisition(streams, new ResolvedDataFormat("csv", "text/csv", null), 1);
        using (var input = await resolver.OpenAsync("original.csv"))
        {
            Assert.That(input.CanSeek, Is.False);
            Assert.That(input.ReadByte(), Is.EqualTo((int)'i'));
        }
        Assert.That(streams.Path, Is.EqualTo("original.csv"));
        Assert.That(upstream.Disposals, Is.EqualTo(1));
    }

    private static void AssertPreview(CliResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        var json = JObject.Parse(result.Output);
        Assert.That(json["rows"]?.Count(), Is.EqualTo(1));
        Assert.That(result.Output + result.Error, Does.Not.Contain("packata-stage-").And.Not.Contain("private-signature"));
    }
    private static string[] TemporaryFiles() => Directory.GetFiles(Path.GetTempPath(), "packata-stage-*.tmp");
    private sealed class SequentialInput(byte[] bytes) : MemoryStream(bytes)
    {
        public int Disposals { get; private set; }
        public override bool CanSeek => false;
        protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
    }
    private sealed class OneStream(Stream stream) : IEndpointStreamResolver
    {
        public string? Path { get; private set; }
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            Path = path;
            return ValueTask.FromResult(stream);
        }
    }
}
