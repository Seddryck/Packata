using System.Text;
using Packata.Core.Contracts;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ExportCommand : AsyncCommand<ExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var schema = ContractSelection.Asset(mapped.Value!, settings.Asset!).ToJsonSchema();
            using var stream = new MemoryStream();
            schema.Serialize(stream);
            var content = Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Output is null) Console.Write(content);
            else
            {
                try { ContractDocumentWriter.Write(settings.Output, content, overwrite: false); }
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
