using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DataValidateSettings : ContractSourceSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Asset to validate; default is all declared assets.")]
    public string? Asset { get; init; }

    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: text or json.")]
    [DefaultValue("text")]
    public string OutputFormat { get; init; } = "text";

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        if (Asset is not null && string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset must be non-empty.");
        return OutputFormat is "text" or "json" ? ValidationResult.Success()
            : ValidationResult.Error("--output-format must be text or json.");
    }
}
