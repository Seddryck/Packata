using Spectre.Console;
using Spectre.Console.Cli;
namespace Packata.Cli;
internal sealed class ExportSettings : ContractSourceSettings
{
    [CommandOption("-f|--output-format <FORMAT>")]
    public string? OutputFormat { get; init; }
    [CommandOption("--asset <ID>")]
    public string? Asset { get; init; }
    [CommandOption("-o|--output <PATH>")]
    public string? Output { get; init; }
    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (OutputFormat != "json-schema") return ValidationResult.Error("--output-format must be json-schema.");
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        return OptionValidation.Output(Output);
    }
}
