using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal class ContractComparisonSettings : PresentationSettings
{
    [CommandArgument(0, "<BASELINE>")]
    [Description("Previous contract: local file, HTTP(S) descriptor, or ZIP package.")]
    public required string Baseline { get; init; }

    [CommandArgument(1, "<CANDIDATE>")]
    [Description("Current contract; additions are present only here, removals only in the baseline.")]
    public required string Candidate { get; init; }

    [CommandOption("--input-format <FORMAT>")]
    [Description("Input standard for both documents: auto, odcs, or datapackage; per-input options override it.")]
    [DefaultValue("auto")]
    public string InputFormat { get; init; } = "auto";

    [CommandOption("--baseline-format <FORMAT>")]
    [Description("Baseline document standard: auto, odcs, or datapackage.")]
    public string? BaselineFormat { get; init; }

    [CommandOption("--candidate-format <FORMAT>")]
    [Description("Candidate document standard: auto, odcs, or datapackage.")]
    public string? CandidateFormat { get; init; }

    [CommandOption("--quiet")]
    [Description("Suppress results, including JSON; retain diagnostics on stderr.")]
    public bool Quiet { get; init; }

    public override ValidationResult Validate()
    {
        if (InputFormat is not ("auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--input-format must be auto, odcs, or datapackage.");
        if (BaselineFormat is not (null or "auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--baseline-format must be auto, odcs, or datapackage.");
        if (CandidateFormat is not (null or "auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--candidate-format must be auto, odcs, or datapackage.");
        return base.Validate();
    }
}
