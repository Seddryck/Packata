using System.ComponentModel;
using Packata.DataPackage.Export;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DialectExportSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("ODCS YAML or Data Package v2 JSON/YAML contract document.")]
    public required string Source { get; init; }

    [CommandOption("--endpoint <ID>")]
    [Description("Exact identifier of the CSV endpoint to export (required).")]
    public required string Endpoint { get; init; }

    [CommandOption("--output <PATH>")]
    [Description("Write standalone JSON to a file; default is stdout.")]
    public string? Output { get; init; }

    [CommandOption("--overwrite")]
    [Description("Allow replacing an existing output file.")]
    public bool Overwrite { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint)) return ValidationResult.Error("--endpoint is required.");
        if (Output is not null && string.IsNullOrWhiteSpace(Output)) return ValidationResult.Error("--output must be a file path.");
        if (Overwrite && Output is null) return ValidationResult.Error("--overwrite requires --output.");
        return ValidationResult.Success();
    }
}

internal sealed class DialectExportCommand : AsyncCommand<DialectExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DialectExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // Refuse accidental source replacement even when --overwrite is supplied.
            var sourceUri = Uri.TryCreate(settings.Source, UriKind.Absolute, out var uri) ? uri : new Uri(Path.GetFullPath(settings.Source));
            if (settings.Output is not null && sourceUri.IsFile && Path.GetFullPath(settings.Output).Equals(
                sourceUri.LocalPath, StringComparison.OrdinalIgnoreCase))
                throw new ContractOperationException("CLI024", "$", "Dialect output must differ from the source contract path.");
            var source = await ContractSource.LoadAsync(settings.Source, "auto", cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var result = new CsvDialectExporter().Export(mapped.Value!, settings.Endpoint);
            foreach (var diagnostic in result.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!result.IsSuccessful) return ExitCodes.InvalidDocument;
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Output is null) Console.WriteLine(result.Value);
            else ContractDocumentWriter.Write(settings.Output, result.Value! + Environment.NewLine, settings.Overwrite);
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(exception is IOException && settings.Output is not null
                ? new("CLI003", Packata.Core.Contracts.MappingSeverity.Error, "$", "Unable to write dialect output or read the source. Check paths, access permissions, and --overwrite for an existing output file.")
                : CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
