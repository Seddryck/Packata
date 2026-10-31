using System.ComponentModel;
using Packata.Application.Generation;
using Packata.Core;
using Spectre.Console;
using Spectre.Console.Cli;
namespace Packata.Cli;
internal sealed class GenerateSettings : ContractSourceSettings
{
    [CommandOption("--target <TARGET>")]
    [Description("Code target from the shared generation catalog.")]
    public string? Target { get; init; }
    [CommandOption("--language <LANGUAGE>")]
    public string? Language { get; init; }
    [CommandOption("--asset <ID>")]
    public string? Asset { get; init; }
    [CommandOption("-o|--output <PATH>")]
    public string? Output { get; init; }
    [CommandOption("--template <PATH>")]
    public string? Template { get; init; }
    [CommandOption("--engine <ENGINE>")]
    public string? Engine { get; init; }
    [CommandOption("--type-mapping <PROFILE>")]
    public string? TypeMapping { get; init; }
    [CommandOption("--namespace <NAMESPACE>")]
    public string? Namespace { get; init; }
    [CommandOption("--type-name <NAME>")]
    public string? TypeName { get; init; }
    [CommandOption("--define <KEY=VALUE>")]
    public string[] Defines { get; init; } = [];
    [CommandOption("--mapping-fallback <POLICY>")]
    public string MappingFallback { get; init; } = "reject";
    internal GenerationOptions GenerationOptions => new(Asset, Target, Engine, TypeMapping, Namespace, TypeName, Defines,
        MappingFallback switch { "object" => TypeMappingFallback.Object, "string" => TypeMappingFallback.String,
            "logical-type" => TypeMappingFallback.LogicalType, _ => TypeMappingFallback.Reject }, Language);
    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (MappingFallback is not ("reject" or "object" or "string" or "logical-type")) return ValidationResult.Error("Unknown --mapping-fallback policy.");
        if (Target is not null && Language is null) return ValidationResult.Error("Bundled generation requires --target and --language.");
        var error = SchemaGenerationService.ValidateOptions(GenerationOptions, Template is null ? null : new(Template, ""));
        return error is null ? OptionValidation.Output(Output) : ValidationResult.Error(error);
    }
}
