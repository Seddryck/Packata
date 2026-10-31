using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Spectre.Console;

namespace Packata.Cli.Testing;

public class CommandDiagnosticColorTests
{
    [TestCase(MappingSeverity.Warning, "93")]
    [TestCase(MappingSeverity.Information, "94")]
    public void Terminal_diagnostics_use_severity_colors_and_render_markup_literally(MappingSeverity severity, string ansi)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.Standard,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }
        });
        console.Profile.Width = 200;
        CommandOutput.Diagnostic(new("SCHEMA_TEST", severity, "members/id", "Keep [red]literal[/] markup"), console);
        Assert.That(writer.ToString(), Does.Contain("\u001b[" + ansi + "m").And.Contain("[red]literal[/]").And.Not.Contain("\u001b[91m"));
    }

    [TestCase(MappingSeverity.Warning)]
    [TestCase(MappingSeverity.Information)]
    public void Non_ANSI_diagnostics_remain_plain_text(MappingSeverity severity)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors
        });
        CommandOutput.Diagnostic(new("SCHEMA_TEST", severity, "members/id", "Retained"), console);
        Assert.That(writer.ToString(), Is.EqualTo($"SCHEMA_TEST {severity} members/id: Retained{Environment.NewLine}"));
    }

    [Test]
    public async Task Redirected_export_keeps_schema_JSON_clean_and_stderr_uncolored_even_when_color_is_forced()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("schema.json", """
            {"name":"members","resources":[{"name":"members","path":"members.csv","schema":{"fields":[{"name":"id","type":"integer","constraints":{"unique":true}}]}}]}
            """);
        var result = await CliWorkspace.RunWithEnvironment(new Dictionary<string, string?> { ["FORCE_COLOR"] = "1", ["CLICOLOR_FORCE"] = "1", ["TERM"] = "xterm-256color" },
            "schema", "export", source, "--output-format", "avro");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.Contain(" Information ").And.Not.Contain("\u001b["));
        Assert.That(Newtonsoft.Json.Linq.JObject.Parse(result.Output)["type"]!.Value<string>(), Is.EqualTo("record"));
    }
}
