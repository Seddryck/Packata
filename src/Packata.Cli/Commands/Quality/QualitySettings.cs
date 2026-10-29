using System.ComponentModel;
using Packata.Core.Execution;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class QualitySettings : DatabaseCommandSettings
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
    [CommandOption("--output-format <FORMAT>")]
    [Description("OORS serialization: json or yaml (default: json).")]
    public string OutputFormat { get; init; } = "json";
    [CommandOption("-o|--output <PATH>")]
    [Description("Write the OORS report to a new file instead of stdout.")]
    public string? Output { get; init; }
    [CommandOption("--sql-read-only-credentials")]
    [Description("Assert that database credentials enforce read-only SQL access. SQLite is enforced automatically.")]
    public bool SqlReadOnlyCredentials { get; init; }

    internal QualityFilters Filters => new() { Id = Id, Asset = Asset, Field = Field, Type = Type, Dimension = Dimension };
    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        if (OutputFormat is not ("json" or "yaml")) return ValidationResult.Error("--output-format must be json or yaml (OORS).");
        try { Filters.Validate(); }
        catch (ArgumentException e) { return ValidationResult.Error(e.Message); }
        return OptionValidation.Output(Output);
    }
}
