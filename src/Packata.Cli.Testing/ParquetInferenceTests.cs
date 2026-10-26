using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public sealed class ParquetInferenceTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Infer_UsesBoundFileAndPreservesMetadata(bool odcs, bool write)
    {
        using var workspace = new CliWorkspace();
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "iris.parquet"),
            Path.Combine(workspace.Path, "iris.parquet"));
        var content = odcs ? CliWorkspace.Odcs.Replace("customers.csv", "iris.parquet").Replace("format: csv", "format: parquet")
            : """{"name":"flowers","title":"Keep title","resources":[{"name":"customers","path":"iris.parquet","mediatype":"application/vnd.apache.parquet"}]}""";
        var source = workspace.Write(odcs ? "contract.yaml" : "package.json", content);
        var output = Path.Combine(workspace.Path, odcs ? "enriched.yaml" : "enriched.json");
        var result = await CliWorkspace.Run(["schema", "infer", source, "--asset", "customers",
            .. (write ? new[] { "--write" } : new[] { "--output", output })]);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        var target = write ? source : output;
        var document = ContractDocument.Load(target);
        Assert.That(document.Contract.Assets.Single().Schema!.Fields, Has.Count.EqualTo(5));
        Assert.That(File.ReadAllText(target), Does.Not.Contain("delimited"));
        if (!write) Assert.That(File.ReadAllText(source), Is.EqualTo(content));
        if (!odcs) Assert.That((string?)JObject.Parse(File.ReadAllText(target))["title"], Is.EqualTo("Keep title"));
    }

    [TestCase("schema")]
    [TestCase("alias")]
    public async Task Infer_DisplayAndAliasDoNotModifyInput(string route)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("customers.csv", "iris.parquet").Replace("format: csv", "format: parquet"));
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "iris.parquet"), Path.Combine(workspace.Path, "iris.parquet"));
        var before = File.ReadAllText(source);
        var result = await CliWorkspace.Run([.. (route == "schema" ? new[] { "schema", "infer" } : new[] { "infer" }), source, "--asset", "customers"]);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("parquet").And.Not.Contain("CSV dialect"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(before));
    }

    [Test]
    public async Task Infer_RejectsCsvOptionsWithoutWriting()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("customers.csv", "iris.parquet").Replace("format: csv", "format: parquet"));
        var result = await CliWorkspace.Run(["schema", "infer", source, "--asset", "customers", "--header", "auto", "--write"]);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("do not apply"));
        Assert.That(File.ReadAllText(source), Does.Not.Contain("properties:"));
    }
}
