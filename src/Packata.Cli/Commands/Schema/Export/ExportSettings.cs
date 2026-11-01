using Spectre.Console;
using Spectre.Console.Cli;
using Packata.Application.Generation;
namespace Packata.Cli;
internal sealed class ExportSettings : ContractSourceSettings
{
    [CommandOption("-f|--output-format <FORMAT>")]
    [System.ComponentModel.Description("Schema document format: json-schema, avro, avro-idl, spark-json, arrow-ipc. Use schema generate for source code.")]
    public string? OutputFormat { get; init; }
    [CommandOption("--profile <PROFILE>")]
    public string? Profile { get; init; }
    [CommandOption("--asset <ID>")]
    public string? Asset { get; init; }
    [CommandOption("-o|--output <PATH>")]
    public string? Output { get; init; }
    [CommandOption("--schema-name <NAME>")]
    public string? SchemaName { get; init; }
    [CommandOption("--schema-namespace <NAMESPACE>")]
    public string? SchemaNamespace { get; init; }
    internal GenerationOptions Options => new(Asset, Operation: "export", OutputFormat: OutputFormat, CompatibilityProfile: Profile, DocumentOptions: SchemaName is null && SchemaNamespace is null ? null : new(SchemaName, SchemaNamespace));
    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (OutputFormat == "arrow-ipc" && Output is null && !Console.IsOutputRedirected)
            return ValidationResult.Error("Binary schema output requires --output or redirected stdout.");
        var error = SchemaGenerationService.ValidateOptions(Options, null);
        return error is null ? OptionValidation.Output(Output) : ValidationResult.Error(error);
    }
}
