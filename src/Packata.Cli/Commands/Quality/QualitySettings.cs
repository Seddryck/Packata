using System.ComponentModel;
using Packata.Core.Execution;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal abstract class QualityFilterSettings : ContractSourceSettings
{
    [CommandOption("--id <ID>")]
    [Description("Select an exact rule ID; repeatable (OR).")]
    public string[] Id { get; init; } = [];
    [CommandOption("--asset <NAME>")]
    [Description("Select an exact asset name; repeatable (OR).")]
    public string[] Asset { get; init; } = [];
    [CommandOption("--field <NAME>")]
    [Description("Select an exact field path; requires --asset; repeatable (OR).")]
    public string[] Field { get; init; } = [];
    [CommandOption("--type <TYPE>")]
    [Description("Select an ODCS rule type; repeatable (OR).")]
    public string[] Type { get; init; } = [];
    [CommandOption("--dimension <DIMENSION>")]
    [Description("Select a quality dimension; repeatable (OR).")]
    public string[] Dimension { get; init; } = [];
    [CommandOption("--tag <TAG>")]
    [Description("Select an exact rule tag; repeatable (OR).")]
    public string[] Tag { get; init; } = [];

    internal QualityFilters Filters => new() { Id = Id, Asset = Asset, Field = Field, Type = Type, Dimension = Dimension, Tag = Tag };

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        try { Filters.Validate(); }
        catch (ArgumentException e) { return ValidationResult.Error(e.Message); }
        return ValidationResult.Success();
    }
}

internal sealed class QualitySettings : QualityFilterSettings
{
    [CommandOption("--connector <SCHEME=ID>")]
    [Description("Override a connector preference for this invocation; repeatable.")]
    public string[] Connectors { get; init; } = [];
    [CommandOption("--connection-env <ENDPOINT=VARIABLE>")]
    [Description("Read a runtime endpoint connection URL from an environment variable; repeatable.")]
    public string[] ConnectionEnvironment { get; init; } = [];
    [CommandOption("--output-format <FORMAT>")]
    [Description("OORS serialization: json or yaml (default: json).")]
    public string OutputFormat { get; init; } = "json";
    [CommandOption("-o|--output <PATH>")]
    [Description("Write the OORS report to a new file instead of stdout.")]
    public string? Output { get; init; }
    [CommandOption("--sql-read-only-credentials")]
    [Description("Assert that database credentials enforce read-only SQL access. SQLite is enforced automatically.")]
    public bool SqlReadOnlyCredentials { get; init; }

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        if (OutputFormat is not ("json" or "yaml")) return ValidationResult.Error("--output-format must be json or yaml (OORS).");
        return OptionValidation.Output(Output);
    }
}

internal sealed class QualityPlanSettings : QualityFilterSettings
{
    [CommandOption("--output-format <FORMAT>")]
    [Description("Plan output format: text or json (default: text).")]
    public string OutputFormat { get; init; } = "text";

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        return OutputFormat is "text" or "json" ? ValidationResult.Success() : ValidationResult.Error("--output-format must be text or json.");
    }
}
