using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DialectExportSettings : ContractSourceSettings
{
    [CommandOption("--endpoint <ID>")]
    [Description("Exact identifier of the CSV endpoint to export (required).")]
    public required string Endpoint { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write standalone JSON to a file; default is stdout.")]
    public string? Output { get; init; }

    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: json (Data Package v2 Table Dialect descriptor).")]
    [DefaultValue("json")]
    public string OutputFormat { get; init; } = "json";

    [CommandOption("--overwrite")]
    [Description("Allow replacing an existing output file.")]
    public bool Overwrite { get; init; }

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (OutputFormat != "json") return ValidationResult.Error("--output-format must be json.");
        if (string.IsNullOrWhiteSpace(Endpoint)) return ValidationResult.Error("--endpoint is required.");
        if (Output is not null && string.IsNullOrWhiteSpace(Output)) return ValidationResult.Error("--output must be a file path.");
        if (Overwrite && Output is null) return ValidationResult.Error("--overwrite requires --output.");
        return ValidationResult.Success();
    }
}
