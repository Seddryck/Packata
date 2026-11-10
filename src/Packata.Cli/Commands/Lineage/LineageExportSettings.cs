using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class LineageExportSettings : ContractSourceSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Exact identifier of the contract asset to export (required).")]
    public required string Asset { get; init; }

    [CommandOption("--job-namespace <NAMESPACE>")]
    [Description("OpenLineage job namespace (required).")]
    public required string JobNamespace { get; init; }

    [CommandOption("--job-name <NAME>")]
    [Description("OpenLineage job name (required).")]
    public required string JobName { get; init; }

    [CommandOption("--dataset-namespace <NAMESPACE>")]
    [Description("OpenLineage namespace shared by the input and output datasets (required).")]
    public required string DatasetNamespace { get; init; }

    [CommandOption("--output-dataset <NAME>")]
    [Description("OpenLineage name of the output dataset (required).")]
    public required string OutputDataset { get; init; }

    [CommandOption("--source-dataset <MAPPING>")]
    [Description("Map a declared source reference to an OpenLineage dataset name as REFERENCE=NAME. Repeat for each source.")]
    public string[] SourceDatasets { get; init; } = [];

    [CommandOption("-o|--output <PATH>")]
    [Description("Write JSON to a new file instead of stdout. Existing files are protected.")]
    public string? Output { get; init; }

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        if (string.IsNullOrWhiteSpace(JobNamespace)) return ValidationResult.Error("--job-namespace is required.");
        if (string.IsNullOrWhiteSpace(JobName)) return ValidationResult.Error("--job-name is required.");
        if (string.IsNullOrWhiteSpace(DatasetNamespace)) return ValidationResult.Error("--dataset-namespace is required.");
        if (string.IsNullOrWhiteSpace(OutputDataset)) return ValidationResult.Error("--output-dataset is required.");
        return OptionValidation.Output(Output);
    }
}
