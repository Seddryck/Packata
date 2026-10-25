using Packata.DataPackage.Export;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DialectExportCommand : AsyncCommand<DialectExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DialectExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            ValidateOutputPath(settings);
            var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var result = new CsvDialectExporter().Export(mapped.Value!, settings.Endpoint);
            foreach (var diagnostic in result.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!result.IsSuccessful) return ExitCodes.InvalidDocument;
            cancellationToken.ThrowIfCancellationRequested();
            WriteOutput(settings, result.Value!);
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

    private static void ValidateOutputPath(DialectExportSettings settings)
    {
        // Refuse accidental source replacement even when --overwrite is supplied.
        var sourceUri = Uri.TryCreate(settings.Source, UriKind.Absolute, out var uri) ? uri : new Uri(Path.GetFullPath(settings.Source));
        if (settings.Output is not null && sourceUri.IsFile && Path.GetFullPath(settings.Output).Equals(
            sourceUri.LocalPath, StringComparison.OrdinalIgnoreCase))
            throw new ContractOperationException("CLI024", "$", "Dialect output must differ from the source contract path.");
    }

    private static void WriteOutput(DialectExportSettings settings, string json)
    {
        if (settings.Output is null) Console.WriteLine(json);
        else ContractDocumentWriter.Write(settings.Output, json + Environment.NewLine, settings.Overwrite);
    }
}
