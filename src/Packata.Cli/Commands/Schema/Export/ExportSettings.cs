using Spectre.Console;
using Spectre.Console.Cli;
using Packata.Application;
namespace Packata.Cli;
internal sealed class ExportSettings : ContractSourceSettings
{
    [CommandOption("-f|--output-format <FORMAT>")]
    public string? OutputFormat { get; init; }
    [CommandOption("--asset <ID>")]
    public string? Asset { get; init; }
    [CommandOption("-o|--output <PATH>")]
    public string? Output { get; init; }
    [CommandOption("--profile <PROFILE>")]
    public string? Profile { get; init; }
    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (OutputFormat is null) return ValidationResult.Error("--output-format is required.");
        if (SchemaDocumentService.ValidateOptions(new(OutputFormat, Asset, Profile)) is { } error) return ValidationResult.Error(error);
        if (OutputFormat == "arrow-ipc" && Output is null && !Console.IsOutputRedirected)
            return ValidationResult.Error("Binary schema output requires --output or redirected stdout.");
        return OptionValidation.Output(Output);
    }
}
