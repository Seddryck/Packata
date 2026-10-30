using Packata.Core.Contracts;
using Packata.Application;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class SatisfiesCommand : AsyncCommand<SatisfiesSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, SatisfiesSettings settings, CancellationToken cancellationToken)
        => new SatisfactionExecution(settings, cancellationToken).RunAsync();

    internal static int ResultExitCode(ContractSatisfaction result, string unknown)
    {
        if (!result.Completed) return 2;
        var unresolved = result.Findings.Any(x => x.Status == SatisfactionStatus.Unknown);
        if (unresolved && unknown == "error") return 2;
        if (result.Findings.Any(x => x.Status == SatisfactionStatus.Unsatisfied)) return 1;
        return unresolved && unknown != "ignore" ? 1 : 0;
    }

    private sealed class SatisfactionExecution(SatisfiesSettings settings, CancellationToken cancellationToken)
    {
        private readonly List<SatisfactionDiagnostic> diagnostics = [];
        private string role = "producer";
        private string source = settings.Producer;
        internal async Task<int> RunAsync()
        {
            try
            {
                var producer = await Load(settings.Producer).ConfigureAwait(false);
                if (producer is null) return Write(null, 2);
                role = "consumer";
                source = settings.Consumer;
                var consumer = await Load(settings.Consumer).ConfigureAwait(false);
                if (consumer is null) return Write(null, 2);
                role = "analysis";
                var result = ContractComparisonService.Satisfies(producer, consumer, cancellationToken: cancellationToken);
                diagnostics.AddRange(result.Diagnostics);
                return Write(result, ResultExitCode(result, settings.Unknown));
            }
            catch (Exception exception)
            {
                var failure = CommandOutput.Failure(exception);
                diagnostics.Add(new(role, source, failure.Code, failure.Severity, failure.SourcePath, failure.Message));
                return Write(null, 2);
            }
        }

        private async Task<SatisfactionInput?> Load(string location)
        {
            using var document = await ContractSource.LoadAsync(location, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var prepared = ContractComparisonService.Prepare(document.ParsedDocument, cancellationToken);
            foreach (var diagnostic in prepared.ValidationDiagnostics)
                diagnostics.Add(new(role, location, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            return prepared.Input is null ? null : ContractComparisonService.SatisfactionInput(prepared.Input, location);
        }

        private int Write(ContractSatisfaction? result, int code)
        {
            if (settings.Quiet) return code;
            try
            {
                var payload = SatisfiesPresenter.Result(settings.Producer, settings.Consumer, result, diagnostics, settings.Unknown, code);
                using var output = new OutputDestination(settings.Output);
                if (settings.EffectiveFormat == "json") CommandOutput.Json(payload, output.Writer);
                else SatisfiesPresenter.Text(payload, output.Writer);
                output.Commit();
                if (settings.EffectiveFormat == "text")
                    foreach (var diagnostic in diagnostics)
                        CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity, diagnostic.Path, diagnostic.Message));
                return code;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CommandOutput.Diagnostic(CommandOutput.Failure(exception));
                return 2;
            }
        }
    }
}
