using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal abstract class ContractSourceSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("Contract document to read. Supported locations depend on the command.")]
    public required string Source { get; init; }

    [CommandOption("--input-format <FORMAT>")]
    [Description("Input contract standard: auto, odcs, or datapackage.")]
    [DefaultValue("auto")]
    public string InputFormat { get; init; } = "auto";

    public override ValidationResult Validate() => InputFormat is "auto" or "odcs" or "datapackage"
        ? ValidationResult.Success()
        : ValidationResult.Error("--input-format must be auto, odcs, or datapackage.");
}
