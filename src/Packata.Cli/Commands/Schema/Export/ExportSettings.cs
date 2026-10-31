using Spectre.Console;
using Spectre.Console.Cli;
using Packata.Application.Generation;
namespace Packata.Cli;
internal sealed class ExportSettings : ContractSourceSettings
{
    [CommandOption("-f|--output-format <FORMAT>")]
    [System.ComponentModel.Description("Schema document format: json-schema. Use schema generate for source code.")]
    public string? OutputFormat { get; init; }
    [CommandOption("--profile <PROFILE>")]
    public string? Profile { get; init; }
    [CommandOption("--asset <ID>")]
    public string? Asset { get; init; }
    [CommandOption("-o|--output <PATH>")]
    public string? Output { get; init; }
    internal GenerationOptions Options => new(Asset, Operation: "export", OutputFormat: OutputFormat, CompatibilityProfile: Profile);
    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        var error = SchemaGenerationService.ValidateOptions(Options, null);
        return error is null ? OptionValidation.Output(Output) : ValidationResult.Error(error);
    }
}
