using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InferSettings : DatabaseCommandSettings
{
    [CommandOption("--asset <ID>")]
    [Description(OptionValidation.AssetDescription)]
    public required string Asset { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write an enriched copy without modifying the source document.")]
    public string? Output { get; init; }

    [CommandOption("--write")]
    [Description("Atomically update the source document in place.")]
    public bool Write { get; init; }

    [CommandOption("--maximum-records <COUNT>")]
    [Description("Maximum number of CSV records to sample; ignored for databases.")]
    [DefaultValue(1000)]
    public int MaximumRecords { get; init; } = 1000;

    [CommandOption("--maximum-bytes <COUNT>")]
    [Description("Optional maximum CSV bytes to sample; ignored for databases.")]
    public long? MaximumBytes { get; init; }

    [CommandOption("--culture <NAME>")]
    [Description("Culture used for inferred CSV values; ignored for databases.")]
    [DefaultValue("")]
    public string Culture { get; init; } = string.Empty;

    [CommandOption("--header <MODE>")]
    [Description("CSV header mode: auto, present, or absent; ignored for databases.")]
    [DefaultValue("auto")]
    public string Header { get; init; } = "auto";

    [CommandOption("--no-dialect-inference")]
    [Description("Use contract dialect hints without inferring the CSV dialect.")]
    public bool NoDialectInference { get; init; }

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (Write && Output is not null)
            return ValidationResult.Error("--output and --write cannot be combined.");
        if (MaximumRecords <= 0)
            return ValidationResult.Error("--maximum-records must be positive.");
        if (MaximumBytes <= 0)
            return ValidationResult.Error("--maximum-bytes must be positive when specified.");
        if (Header.ToLowerInvariant() is not ("auto" or "present" or "absent"))
            return ValidationResult.Error("--header must be auto, present, or absent.");
        try
        {
            if (!string.IsNullOrWhiteSpace(Culture))
                _ = System.Globalization.CultureInfo.GetCultureInfo(Culture);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return ValidationResult.Error($"Unknown culture '{Culture}'.");
        }
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        return OptionValidation.Output(Output);
    }
}
