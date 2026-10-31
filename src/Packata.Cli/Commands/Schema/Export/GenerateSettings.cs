using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Packata.Application.Generation;
using Packata.Core;

namespace Packata.Cli;

internal sealed class GenerateSettings : ContractSourceSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Canonical asset ID; optional for single-asset code generation.")]
    public string? Asset { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write output to a new file instead of stdout. Existing files are protected.")]
    public string? Output { get; init; }

    [CommandOption("--target <TARGET>")]
    public string? Target { get; init; }
    [CommandOption("--language <LANGUAGE>")]
    public string? Language { get; init; }
    [CommandOption("--dialect <DIALECT>")]
    public string? Dialect { get; init; }

    [CommandOption("--template <PATH>")]
    [Description("Custom Didot template; relative paths resolve from the descriptor directory.")]
    public string? Template { get; init; }

    [CommandOption("--engine <ENGINE>")]
    [Description("Didot engine override; otherwise use recognized suffix or Scriban.")]
    public string? Engine { get; init; }

    [CommandOption("--type-mapping <PROFILE>")]
    [Description("Prepared profile: csharp-record, csharp-datatable, or csharp-polars.")]
    public string? TypeMapping { get; init; }

    [CommandOption("--namespace <NAMESPACE>")]
    [Description("C# namespace for generated definitions.")]
    public string? Namespace { get; init; }

    [CommandOption("--type-name <NAME>")]
    [Description("Generated root C# type name.")]
    public string? TypeName { get; init; }

    [CommandOption("--define <KEY=VALUE>")]
    [Description("Repeatable user variable; duplicate keys are rejected.")]
    public string[] Defines { get; init; } = [];

    [CommandOption("--mapping-fallback <POLICY>")]
    [Description("Explicit mapping policy: reject (default), object, string, logical-type.")]
    public string MappingFallback { get; init; } = "reject";

    internal GenerationOptions GenerationOptions => new(Asset, Target,
        Engine, TypeMapping, Namespace, TypeName, Defines, MappingFallback switch
        { "object" => TypeMappingFallback.Object, "string" => TypeMappingFallback.String, "logical-type" => TypeMappingFallback.LogicalType, _ => TypeMappingFallback.Reject }, Language, Dialect: Dialect);

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        var options = ValidateGeneration();
        if (!options.Successful) return options;
        return OptionValidation.Output(Output);
    }

    private ValidationResult ValidateGeneration()
    {
        if (MappingFallback is not ("reject" or "object" or "string" or "logical-type")) return ValidationResult.Error("Unknown --mapping-fallback policy.");
        var error = SchemaGenerationService.ValidateOptions(GenerationOptions, Template is null ? null : new(Template, ""));
        return error is null ? ValidationResult.Success() : ValidationResult.Error(error);
    }
}
