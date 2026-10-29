using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DialectInferSettings : ContractSourceSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Exact asset identifier with one CSV-family endpoint binding.")]
    public required string Asset { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write an enriched copy to a new local file.")]
    public string? Output { get; init; }

    [CommandOption("--write")]
    [Description("Atomically replace the local source descriptor.")]
    public bool Write { get; init; }

    [CommandOption("--maximum-records <COUNT>")]
    [Description("Maximum records to profile; dialect candidate scoring uses at most 100.")]
    [DefaultValue(1000)]
    public int MaximumRecords { get; init; } = 1000;

    [CommandOption("--maximum-bytes <COUNT>")]
    [Description("Maximum source bytes sampled; default 1048576. Compressed samples may require a larger limit.")]
    [DefaultValue(1048576L)]
    public long MaximumBytes { get; init; } = 1048576;

    [CommandOption("--header <MODE>")]
    [Description("Header mode: auto (honors declared hints), present, or absent.")]
    [DefaultValue("auto")]
    public string Header { get; init; } = "auto";

    [CommandOption("--accept-ambiguous")]
    [Description("Permit saving ambiguous dialect candidates after reviewing the report.")]
    public bool AcceptAmbiguous { get; init; }

    [CommandOption("--allow-shared-endpoint")]
    [Description("Acknowledge that saving changes parsing settings for every asset sharing the endpoint.")]
    public bool AllowSharedEndpoint { get; init; }

    internal InferSettings InferenceSettings() => new()
    {
        Source = Source, Asset = Asset, MaximumRecords = MaximumRecords,
        MaximumBytes = MaximumBytes, Header = Header
    };

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (Write && Output is not null) return ValidationResult.Error("--output and --write cannot be combined.");
        if (MaximumRecords <= 0 || MaximumBytes <= 0)
            return ValidationResult.Error("--maximum-records and --maximum-bytes must be positive.");
        if (Header.ToLowerInvariant() is not ("auto" or "present" or "absent"))
            return ValidationResult.Error("--header must be auto, present, or absent.");
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        return OptionValidation.Output(Output);
    }
}
