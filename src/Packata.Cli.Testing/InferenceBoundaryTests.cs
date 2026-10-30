using NUnit.Framework;

namespace Packata.Cli.Testing;

public sealed class InferenceBoundaryTests
{
    [TestCase("csv", "--sheet-name", "missing")]
    [TestCase("parquet", "--header", "auto")]
    [TestCase("parquet", "--maximum-records", "1000")]
    [TestCase("xlsx", "--culture", "en-US")]
    public async Task UnsupportedOptions_UseApplicationRulesBeforeResourceAccess(string format, string option, string value)
    {
        using var workspace = new CliWorkspace();
        var text = CliWorkspace.PackageJson.Replace("customers.csv", "missing." + format).Replace("\"format\":\"csv\"", "\"format\":\"" + format + "\"");
        var path = workspace.Write("datapackage.json", text);
        var result = await CliWorkspace.Run("schema", "infer", path, "--asset", "customers", option, value, "--write");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Does.Contain("Options " + option).And.Contain("do not apply"));
        Assert.That(File.ReadAllText(path), Is.EqualTo(text));
    }

    [Test]
    public void FlagTranslation_PreservesExplicitDefaultsAndEqualsSyntax()
    {
        var settings = new InferSettings { Source = "unused", Asset = "orders" };
        var options = InferenceService.Options(settings, ["--header=auto", "--maximum-records", "1000"]);
        Assert.That(options.SuppliedOptions, Is.EqualTo(Packata.Application.InferenceOptions.Header | Packata.Application.InferenceOptions.MaximumRecords));
        Assert.That(options.Header, Is.EqualTo("auto"));
        Assert.That(InferenceService.Options(settings).SuppliedOptions, Is.EqualTo(Packata.Application.InferenceOptions.None));
    }
}
