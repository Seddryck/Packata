using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class CompareInvocationTests
{
    [TestCase(" ")]
    [TestCase("=")]
    [TestCase(":")]
    public async Task OutputFormatSyntax_IsConsistentForExecutionValidationAndMissingArguments(string separator)
    {
        var format = Option("--output-format", separator, "json");
        var execution = await CliWorkspace.Run(["contract", "compare", "missing-baseline", "missing-candidate", .. format]);
        var validation = await CliWorkspace.Run(["contract", "compare", "missing-baseline", "missing-candidate", "--policy", "other", .. format]);
        var missing = await CliWorkspace.Run(["contract", "compare", "missing-baseline", .. format]);

        Assert.Multiple(() =>
        {
            Assert.That(execution.ExitCode, Is.EqualTo(3), execution.Error);
            AssertFailure(execution, 3, false);
            Assert.That(validation.ExitCode, Is.EqualTo(2), validation.Error);
            AssertFailure(validation, 2, true);
            Assert.That(missing.ExitCode, Is.EqualTo(2), missing.Error);
            AssertFailure(missing, 2, true);
        });
    }

    [TestCase("--output", " ")]
    [TestCase("--output", "=")]
    [TestCase("--output", ":")]
    [TestCase("-o", " ")]
    [TestCase("-o", "=")]
    [TestCase("-o", ":")]
    public async Task DestinationSyntax_IsConsistentForExecutionAndInvocationFailures(string name, string separator)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("source", CliWorkspace.Odcs);
        var output = Path.Combine(workspace.Path, "report with spaces.json");
        var destination = Option(name, separator, output);
        // --fail-on none permits this fixture's missing schema while testing transport only.
        var execution = await CliWorkspace.Run(["contract", "compare", source, source, "--fail-on", "none",
            "--output-format", "json", .. destination]);
        Assert.Multiple(() =>
        {
            Assert.That(execution.ExitCode, Is.Zero, execution.Error);
            Assert.That(execution.Output, Is.Empty);
            Assert.That((bool?)JObject.Parse(File.ReadAllText(output))["completed"], Is.True);
        });

        var invalidPath = Path.Combine(workspace.Path, "invalid with spaces.json");
        var invalid = await CliWorkspace.Run(["contract", "compare", source, source, "--policy", "other",
            "--output-format", "json", .. Option(name, separator, invalidPath)]);
        Assert.Multiple(() =>
        {
            Assert.That(invalid.ExitCode, Is.EqualTo(2), invalid.Error);
            Assert.That(invalid.Output, Is.Empty);
            Assert.That(invalid.Error, Does.Contain("CLI002"));
            Assert.That(File.Exists(invalidPath), Is.False);
        });
    }

    [TestCase(" ", true)]
    [TestCase("=", true)]
    [TestCase(":", true)]
    [TestCase(" ", false)]
    [TestCase("=", false)]
    [TestCase(":", false)]
    public async Task QuietBooleanSyntax_IsRespectedByExecutionAndInvocationFailures(string separator, bool quiet)
    {
        var options = Option("--quiet", separator, quiet.ToString());
        var execution = await CliWorkspace.Run(["contract", "compare", "missing-baseline", "missing-candidate",
            "--output-format", "json", .. options]);
        var validation = await CliWorkspace.Run(["contract", "compare", "missing-baseline", "missing-candidate",
            "--policy", "other", "--output-format", "json", .. options]);

        Assert.That(execution.ExitCode, Is.EqualTo(3));
        Assert.That(validation.ExitCode, Is.EqualTo(2));
        if (quiet)
        {
            Assert.That(execution.Output, Is.Empty);
            Assert.That(validation.Output, Is.Empty);
        }
        else
        {
            AssertFailure(execution, 3, false);
            AssertFailure(validation, 2, true);
        }
    }

    [Test]
    public async Task BareQuietFlag_SuppressesInvocationEnvelopes()
    {
        var result = await CliWorkspace.Run("contract", "compare", "a", "b", "--policy", "other",
            "--output-format:json", "--quiet");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("CLI002"));
    }

    [TestCase("--output-format=json")]
    [TestCase("--output-format:json")]
    public async Task EndOfOptions_PreventsRemainingArgumentsFromSelectingJson(string remaining)
    {
        var result = await CliWorkspace.Run("contract", "compare", "a", "b", "--policy", "other", "--", remaining);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("CLI002"));
    }

    [TestCase("--output-format", "json")]
    [TestCase("--output-format", "--quiet")]
    [TestCase("--output", "--output-format=json")]
    [TestCase("--output", "--quiet")]
    public async Task OptionValues_AreNotReinterpretedAsPresentationFlags(string name, string value)
    {
        var result = await CliWorkspace.Run("contract", "compare", "a", "b", "--policy", "other", name + "=" + value);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        // Only the first case requests a supported JSON representation on stdout.
        if (name == "--output-format" && value == "json") AssertFailure(result, 2, true);
        else Assert.That(result.Output, Is.Empty);
    }

    [Test]
    public async Task UnrelatedOptions_DoNotHideTheSelectedInvocationEnvelope()
    {
        var result = await CliWorkspace.Run("contract", "compare", "a", "b", "--unknown", "value", "--policy", "other", "--output-format:json");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        AssertFailure(result, 2, true);
    }

    [TestCase("diff")]
    [TestCase("inspect")]
    [TestCase("validate")]
    public async Task OtherCommands_DoNotAcquireComparisonFailureEnvelopes(string command)
    {
        var result = await CliWorkspace.Run("contract", command, "--unknown", "--output-format:json");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("CLI002"));
    }

    private static string[] Option(string name, string separator, string value)
        => separator == " " ? [name, value] : [name + separator + value];

    private static void AssertFailure(CliResult result, int exitCode, bool invocation)
    {
        var payload = JObject.Parse(result.Output);
        Assert.That((int?)payload["exitCode"], Is.EqualTo(exitCode));
        Assert.That((bool?)payload["completed"], Is.False);
        Assert.That((string?)payload["outcome"], Is.EqualTo("failed"));
        if (invocation)
        {
            Assert.That(payload["baseline"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(payload["candidate"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That((string?)payload["diagnostics"]?[0]?["code"], Is.EqualTo("CLI002"));
        }
    }
}
