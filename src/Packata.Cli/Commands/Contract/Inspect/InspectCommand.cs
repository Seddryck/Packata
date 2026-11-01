using Spectre.Console.Cli;
using Packata.Application;

namespace Packata.Cli;

internal sealed class InspectCommand : AsyncCommand<InspectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InspectSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var mapped = ContractInspector.Inspect(source.Document!, cancellationToken, new(settings.Asset)).Mapping;
            var contract = mapped.Value ?? throw new ContractOperationException("CLI012", "$", "The contract could not be mapped to the canonical model.");
            var result = CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, contract, diagnostics = mapped.Diagnostics });
            using var output = new OutputDestination(settings.Output);
            if (settings.OutputFormat == "json")
            {
                CommandOutput.Json(result, output.Writer);
            }
            else
            {
                InspectPresenter.Text(result, output.Console);
                foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            }
            output.Commit();
            return mapped.IsSuccessful ? ExitCodes.Success : ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
