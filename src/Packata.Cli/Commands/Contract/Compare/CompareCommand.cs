using Packata.Core.Contracts;
using Packata.Application;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class CompareCommand : AsyncCommand<CompareSettings>
{
    internal const int GateFailure = 4;
    internal const int AnalysisFailure = 5;

    public override Task<int> ExecuteAsync(CommandContext context, CompareSettings settings, CancellationToken cancellationToken)
        => new ComparisonExecution(settings, cancellationToken).RunAsync();

    private sealed class ComparisonExecution(CompareSettings settings, CancellationToken cancellationToken)
    {
        private readonly List<ComparisonDiagnostic> diagnostics = [];
        private string input = "baseline";
        private string source = settings.Baseline;

        internal async Task<int> RunAsync()
        {
            try
            {
                var baseline = await Load(settings.Baseline, settings.BaselineFormat ?? settings.InputFormat).ConfigureAwait(false);
                if (baseline is null) return Write(null, ExitCodes.InvalidDocument);
                input = "candidate";
                source = settings.Candidate;
                var candidate = await Load(settings.Candidate, settings.CandidateFormat ?? settings.InputFormat).ConfigureAwait(false);
                if (candidate is null) return Write(null, ExitCodes.InvalidDocument);
                input = "comparison";
                source = settings.Baseline + " -> " + settings.Candidate;
                var result = ContractComparisonService.Compare(baseline, candidate, cancellationToken);
                AddDiagnostics(result);
                return Write(result, ResultExitCode(result));
            }
            catch (Exception exception)
            {
                return HandleFailure(exception);
            }
        }

        private void AddDiagnostics(CompatibilityResult result)
        {
            foreach (var diagnostic in result.Diagnostics)
                diagnostics.Add(new(diagnostic.Input, diagnostic.Input switch
                {
                    "baseline" => settings.Baseline,
                    "candidate" => settings.Candidate,
                    _ => source
                }, diagnostic.Code, diagnostic.Severity, diagnostic.Path, diagnostic.Message));
        }

        private int ResultExitCode(CompatibilityResult result)
        {
            if (!result.Completed)
                return result.Diagnostics.Any(x => x.Code == "CMP130") ? ExitCodes.Cancelled : AnalysisFailure;
            return result.Findings.Any(x => settings.Gates(x.Classification)) ? GateFailure : ExitCodes.Success;
        }

        private int HandleFailure(Exception exception)
        {
            if (input == "comparison" && exception is not OperationCanceledException)
            {
                diagnostics.Add(new(input, source, "CMP999", MappingSeverity.Error, "$", "Compatibility analysis could not complete."));
                return Write(null, AnalysisFailure);
            }
            var diagnostic = exception is InvalidDataException
                ? new MappingDiagnostic("CLI021", MappingSeverity.Error, "$", "Invalid or unsupported archive descriptor package.")
                : CommandOutput.Failure(exception);
            diagnostics.Add(new(input, source, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            return Write(null, exception is InvalidDataException ? ExitCodes.InvalidDocument : CommandOutput.FailureExitCode(exception));
        }

        private async Task<CompatibilityInput?> Load(string location, string format)
        {
            using var document = await ContractSource.LoadAsync(location, format, cancellationToken).ConfigureAwait(false);
            var prepared = ContractComparisonService.Prepare(document.ParsedDocument, cancellationToken);
            foreach (var diagnostic in prepared.ValidationDiagnostics)
                diagnostics.Add(new(input, source, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            return prepared.Input;
        }

        private int Write(CompatibilityResult? result, int exitCode)
        {
            try { return WriteResult(result, exitCode); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CommandOutput.Diagnostic(CommandOutput.Failure(exception));
                return ExitCodes.Io;
            }
        }

        private int WriteResult(CompatibilityResult? result, int exitCode)
        {
            var payload = ComparePresenter.Result(settings.Baseline, settings.Candidate, result, diagnostics, settings.FailOn, exitCode);
            if (!settings.Quiet)
            {
                using var output = new OutputDestination(settings.Output);
                if (settings.OutputFormat == "json") CommandOutput.Json(payload, output.Writer);
                else ComparePresenter.Text(payload, output.Writer);
                output.Commit();
            }
            if (settings.Quiet || settings.OutputFormat == "text")
                foreach (var diagnostic in diagnostics)
                    CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity,
                        $"{diagnostic.Input} ({diagnostic.Source}) {diagnostic.SourcePath}", diagnostic.Message));
            return exitCode;
        }
    }
}
