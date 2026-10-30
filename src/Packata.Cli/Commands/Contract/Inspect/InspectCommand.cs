using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InspectCommand : AsyncCommand<InspectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InspectSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source, cancellationToken);
            var contract = mapped.Value!;
            if (settings.Asset is not null)
            {
                var asset = ContractSelection.Asset(contract, settings.Asset);
                var endpointIds = asset.EndpointBindings.Select(binding => binding.EndpointId).ToHashSet(StringComparer.Ordinal);
                contract = contract with { Assets = [asset], Endpoints = contract.Endpoints.Where(endpoint => endpointIds.Contains(endpoint.Id)).ToArray() };
            }
            var result = CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, contract, diagnostics = mapped.Diagnostics });
            using var output = new OutputDestination(settings.Output);
            if (settings.OutputFormat == "json") CommandOutput.Json(result, output.Writer);
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
