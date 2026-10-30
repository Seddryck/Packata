using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Packata.Application.Generation;
using Packata.Core;

namespace Packata.Cli;

internal sealed class ExportSettings : ContractSourceSettings
{
    [CommandOption("-f|--output-format <FORMAT>")]
    [Description("Non-template target format: json-schema. Conflicts with generation selectors.")]
    public string? OutputFormat { get; init; }

    [CommandOption("--asset <ID>")]
    [Description("Canonical asset ID; optional for single-asset code generation.")]
    public string? Asset { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write output to a new file instead of stdout. Existing files are protected.")]
    public string? Output { get; init; }

    [CommandOption("--record")]
    [Description("Generate C# records using the bundled Didot template.")]
    public bool Record { get; init; }

    [CommandOption("--datatable")]
    [Description("Generate a C# DataTable schema factory.")]
    public bool DataTable { get; init; }

    [CommandOption("--polars")]
    [Description("Generate a C# Polars.NET 1.0.1 schema factory.")]
    public bool Polars { get; init; }

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

    internal bool GeneratesCode => Record || DataTable || Polars || Template is not null;
    internal GenerationOptions GenerationOptions => new(Asset, Record ? "record" : DataTable ? "datatable" : Polars ? "polars" : null,
        Engine, TypeMapping, Namespace, TypeName, Defines, MappingFallback switch
        { "object" => TypeMappingFallback.Object, "string" => TypeMappingFallback.String, "logical-type" => TypeMappingFallback.LogicalType, _ => TypeMappingFallback.Reject });

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        var selectors = new[] { Record, DataTable, Polars, Template is not null }.Count(value => value);
        if (selectors > 1 || (selectors > 0 && OutputFormat is not null))
            return ValidationResult.Error("Select exactly one generation selector or --output-format json-schema.");
        var options = GeneratesCode ? ValidateGeneration() : ValidateJsonSchema();
        if (!options.Successful) return options;
        return OptionValidation.Output(Output);
    }

    private bool HasGenerationOptions() => Engine is not null || TypeMapping is not null || Namespace is not null
        || TypeName is not null || Defines.Length > 0 || MappingFallback != "reject";

    private ValidationResult ValidateJsonSchema()
    {
        if (OutputFormat != "json-schema") return ValidationResult.Error("--output-format must be json-schema, or choose --record, --datatable, --polars, or --template.");
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        return HasGenerationOptions() ? ValidationResult.Error("Template generation options require a generation selector.") : ValidationResult.Success();
    }

    private ValidationResult ValidateGeneration()
    {
        if (MappingFallback is not ("reject" or "object" or "string" or "logical-type")) return ValidationResult.Error("Unknown --mapping-fallback policy.");
        var error = SchemaGenerationService.ValidateOptions(GenerationOptions, Template is null ? null : new(Template, ""));
        return error is null ? ValidationResult.Success() : ValidationResult.Error(error);
    }
}
