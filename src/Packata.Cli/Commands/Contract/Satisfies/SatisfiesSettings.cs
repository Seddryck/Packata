using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class SatisfiesSettings : PresentationSettings
{
    [CommandArgument(0, "<PRODUCER>")]
    [Description("Complete producer contract: local file, HTTP(S) descriptor, or supported archive.")]
    public required string Producer { get; init; }

    [CommandOption("--consumer <SOURCE>")]
    [Description("Complete consumer contract in the same native standard as the producer.")]
    public required string Consumer { get; init; }

    [CommandOption("--input-format <FORMAT>")]
    [DefaultValue("auto")]
    public string InputFormat { get; init; } = "auto";

    [CommandOption("--format <FORMAT>")]
    [Description("Alias for --output-format: text or json.")]
    public string? Format { get; init; }

    [CommandOption("--unknown <POLICY>")]
    [DefaultValue("unsatisfied")]
    [Description("Gate unresolved findings: unsatisfied (exit 1), ignore, or error (exit 2). Findings remain Unknown.")]
    public string Unknown { get; init; } = "unsatisfied";

    [CommandOption("--quiet")]
    [Description("Suppress all output while preserving exit behavior.")]
    public bool Quiet { get; init; }

    internal string EffectiveFormat => Format ?? OutputFormat;
    public override ValidationResult Validate()
    {
        if (InputFormat is not ("auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--input-format must be auto, odcs, or datapackage.");
        if (Unknown is not ("unsatisfied" or "ignore" or "error"))
            return ValidationResult.Error("--unknown must be unsatisfied, ignore, or error.");
        if (Format is not (null or "text" or "json"))
            return ValidationResult.Error("--format must be text or json.");
        return base.Validate();
    }
}
