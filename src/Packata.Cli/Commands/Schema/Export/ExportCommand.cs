using Packata.Application;
using Packata.Core.Contracts;
using Spectre.Console.Cli;
using Packata.Application.Generation;

namespace Packata.Cli;

internal sealed class ExportCommand : AsyncCommand<ExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var result = await SchemaDocumentService.ExportAsync(source.ParsedDocument,
                new(settings.OutputFormat!, settings.Asset, settings.Profile), cancellationToken).ConfigureAwait(false);
            foreach (var diagnostic in result.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!result.IsSuccessful) return ExitCodes.InvalidDocument;
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Bytes is { } bytes)
            {
                if (settings.Output is null)
                {
                    await Console.OpenStandardOutput().WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                else ContractDocumentWriter.WriteBytes(settings.Output, bytes, cancellationToken);
            }
            else if (settings.Output is null) Console.Write(result.Content + Environment.NewLine);
            else
            {
                try { ContractDocumentWriter.Write(settings.Output, result.Content + Environment.NewLine, overwrite: false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    CommandOutput.Diagnostic(new("CLI003", MappingSeverity.Error, "$.output",
                        "Unable to write output. Choose a new file and check directory permissions."));
                    return ExitCodes.Io;
                }
            }
            return ExitCodes.Success;
        }
        catch (JsonSchemaConversionException exception)
        {
            CommandOutput.Diagnostic(new("CLI020", MappingSeverity.Error, "$.schema", exception.Message));
            return ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
