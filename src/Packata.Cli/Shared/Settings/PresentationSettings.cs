using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal class PresentationSettings : CommandSettings
{
    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: text or json.")]
    [DefaultValue("text")]
    public string OutputFormat { get; init; } = "text";

    [CommandOption("-o|--output <PATH>")]
    [Description("Write the result to a new file instead of stdout.")]
    public string? Output { get; init; }

    public override ValidationResult Validate() => OutputFormat is "text" or "json" ? OptionValidation.Output(Output)
        : ValidationResult.Error("--output-format must be text or json.");
}
