using Packata.Core.Contracts;
using Packata.Application;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DiffCommand : AsyncCommand<ContractComparisonSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, ContractComparisonSettings settings, CancellationToken cancellationToken)
        => new DiffExecution(settings, cancellationToken).RunAsync();

    private sealed class DiffExecution(ContractComparisonSettings settings, CancellationToken cancellationToken)
    {
        private readonly List<ComparisonDiagnostic> diagnostics = [];
        private string input = "baseline";
        private string source = settings.Baseline;

        internal async Task<int> RunAsync()
        {
            try
            {
                var baseline = await Load(settings.Baseline, settings.BaselineFormat ?? settings.InputFormat).ConfigureAwait(false);
                if (baseline is null) return Invalid();
                input = "candidate";
                source = settings.Candidate;
                var candidate = await Load(settings.Candidate, settings.CandidateFormat ?? settings.InputFormat).ConfigureAwait(false);
                if (candidate is null) return Invalid();
                input = "comparison";
                source = settings.Baseline + " -> " + settings.Candidate;
                cancellationToken.ThrowIfCancellationRequested();
                var diff = ContractComparisonService.Diff(baseline, candidate, cancellationToken);
                var result = DiffPresenter.Result(settings.Baseline, settings.Candidate, diff, diagnostics);
                cancellationToken.ThrowIfCancellationRequested();
                if (!settings.Quiet)
                {
                    using var output = new OutputDestination(settings.Output);
                    if (settings.OutputFormat == "json") CommandOutput.Json(result, output.Writer);
                    else DiffPresenter.Text(result, output.Writer);
                    output.Commit();
                }
                if (settings.Quiet || settings.OutputFormat == "text") WriteDiagnostics();
                return ExitCodes.Success;
            }
            catch (Exception exception)
            {
                Add(exception is InvalidDataException
                    ? new("CLI021", MappingSeverity.Error, "$", "Invalid or unsupported archive descriptor package.")
                    : CommandOutput.Failure(exception));
                WriteDiagnostics();
                return exception is InvalidDataException ? ExitCodes.InvalidDocument : CommandOutput.FailureExitCode(exception);
            }
        }

        private async Task<DataContract?> Load(string location, string format)
        {
            using var document = await ContractSource.LoadAsync(location, format, cancellationToken).ConfigureAwait(false);
            var prepared = ContractComparisonService.Prepare(document.ParsedDocument, cancellationToken);
            foreach (var diagnostic in prepared.ValidationDiagnostics) Add(diagnostic);
            if (!prepared.Valid) return null;
            var mapped = prepared.Mapping ?? throw new InvalidOperationException("The comparison mapping is unavailable.");
            foreach (var diagnostic in mapped.Diagnostics) Add(diagnostic);
            if (mapped.Value is null && mapped.Diagnostics.All(x => x.Severity != MappingSeverity.Error))
                Add(new("CLI012", MappingSeverity.Error, "$", "The contract could not be mapped to the canonical model."));
            cancellationToken.ThrowIfCancellationRequested();
            return mapped.IsSuccessful ? mapped.Value : null;
        }

        private void Add(MappingDiagnostic diagnostic) => diagnostics.Add(new(input, source,
            diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));

        private void WriteDiagnostics()
        {
            foreach (var diagnostic in diagnostics)
                CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity,
                    $"{diagnostic.Input} ({diagnostic.Source}) {diagnostic.SourcePath}", diagnostic.Message));
        }

        private int Invalid()
        {
            WriteDiagnostics();
            return ExitCodes.InvalidDocument;
        }
    }
}
