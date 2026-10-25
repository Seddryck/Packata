using System.ComponentModel;
using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class CompareSettings : ContractComparisonSettings
{
    [CommandOption("--policy <POLICY>")]
    [Description("Consumer policy: baseline-consumers-v1. Candidate data must remain safe for baseline consumers.")]
    [DefaultValue(CompatibilityResult.ConsumerPolicy)]
    public string Policy { get; init; } = CompatibilityResult.ConsumerPolicy;

    [CommandOption("--fail-on <CLASSIFICATIONS>")]
    [Description("Comma-separated breaking,potentially-breaking,unknown, or none. Defaults to all three; changes the gate only.")]
    [DefaultValue("breaking,potentially-breaking,unknown")]
    public string FailOn { get; init; } = "breaking,potentially-breaking,unknown";

    public override ValidationResult Validate()
    {
        var inherited = base.Validate();
        if (!inherited.Successful) return inherited;
        if (Policy != CompatibilityResult.ConsumerPolicy)
            return ValidationResult.Error("--policy must be baseline-consumers-v1.");
        return FailOn == "none" || FailOn.Split(',').All(x => x is "breaking" or "potentially-breaking" or "unknown")
            ? ValidationResult.Success() : ValidationResult.Error("--fail-on must list breaking,potentially-breaking,unknown, or be none.");
    }

    internal bool Gates(CompatibilityClassification classification) => FailOn.Split(',').Contains(classification switch
    {
        CompatibilityClassification.Breaking => "breaking",
        CompatibilityClassification.PotentiallyBreaking => "potentially-breaking",
        CompatibilityClassification.Unknown => "unknown",
        _ => "non-breaking"
    }, StringComparer.Ordinal);
}
