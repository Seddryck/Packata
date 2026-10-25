using System.ComponentModel;
using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class CompareSettings : DiffSettings
{
    [CommandOption("--policy <POLICY>")]
    [Description("Consumer policy: baseline-consumers-v1. Candidate data must remain safe for baseline consumers.")]
    [DefaultValue(CompatibilityResult.ConsumerPolicy)]
    public string Policy { get; init; } = CompatibilityResult.ConsumerPolicy;

    [CommandOption("--fail-on <CLASSIFICATIONS>")]
    [Description("Comma-separated breaking,potentially-breaking,unknown, or none. Defaults to all three; changes the gate only.")]
    [DefaultValue("breaking,potentially-breaking,unknown")]
    public string FailOn { get; init; } = "breaking,potentially-breaking,unknown";

    public override ValidationResult Validate()
    {
        var inherited = base.Validate();
        if (!inherited.Successful) return inherited;
        if (Policy != CompatibilityResult.ConsumerPolicy)
            return ValidationResult.Error("--policy must be baseline-consumers-v1.");
        return FailOn == "none" || FailOn.Split(',').All(x => x is "breaking" or "potentially-breaking" or "unknown")
            ? ValidationResult.Success() : ValidationResult.Error("--fail-on must list breaking,potentially-breaking,unknown, or be none.");
    }

    internal bool Gates(CompatibilityClassification classification) => FailOn.Split(',').Contains(classification switch
    {
        CompatibilityClassification.Breaking => "breaking",
        CompatibilityClassification.PotentiallyBreaking => "potentially-breaking",
        CompatibilityClassification.Unknown => "unknown",
        _ => "non-breaking"
    }, StringComparer.Ordinal);
}

internal sealed class CompareCommand : AsyncCommand<CompareSettings>
{
    internal const int GateFailure = 4;
    internal const int AnalysisFailure = 5;

    public override Task<int> ExecuteAsync(CommandContext context, CompareSettings settings, CancellationToken cancellationToken)
        => new ComparisonExecution(settings, cancellationToken).RunAsync();

    private sealed class ComparisonExecution(CompareSettings settings, CancellationToken cancellationToken)
    {
        private readonly List<DiffDiagnostic> diagnostics = [];
        private string input = "baseline";
        private string source = settings.Baseline;

        internal async Task<int> RunAsync()
        {
            try
            {
                var baseline = await Load(settings.Baseline, settings.BaselineFormat).ConfigureAwait(false);
                if (baseline is null) return Write(null, ExitCodes.InvalidDocument);
                input = "candidate";
                source = settings.Candidate;
                var candidate = await Load(settings.Candidate, settings.CandidateFormat).ConfigureAwait(false);
                if (candidate is null) return Write(null, ExitCodes.InvalidDocument);
                input = "comparison";
                source = settings.Baseline + " -> " + settings.Candidate;
                var result = ContractCompatibility.Compare(baseline, candidate, cancellationToken);
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
                ? new MappingDiagnostic("CLI021", MappingSeverity.Error, "$", "Invalid or unsupported ZIP descriptor package.")
                : CommandOutput.Failure(exception);
            diagnostics.Add(new(input, source, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            return Write(null, exception is InvalidDataException ? ExitCodes.InvalidDocument : CommandOutput.FailureExitCode(exception));
        }

        private async Task<CompatibilityInput?> Load(string location, string format)
        {
            var document = await ContractSource.LoadAsync(location, format, cancellationToken).ConfigureAwait(false);
            var validation = ValidateCommand.Validate(document);
            foreach (var diagnostic in validation)
                diagnostics.Add(new(input, source, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            if (validation.Any(x => x.Severity == MappingSeverity.Error)) return null;
            var comparisonInput = document.CompatibilityInput();
            cancellationToken.ThrowIfCancellationRequested();
            return comparisonInput;
        }

        private int Write(CompatibilityResult? result, int exitCode)
        {
            var payload = ComparePresenter.Result(settings.Baseline, settings.Candidate, result, diagnostics, settings.FailOn, exitCode);
            if (!settings.Quiet)
            {
                if (settings.Output == "json") CommandOutput.Json(payload);
                else ComparePresenter.Text(payload);
            }
            if (settings.Quiet || settings.Output == "text")
                foreach (var diagnostic in diagnostics)
                    CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity,
                        $"{diagnostic.Input} ({diagnostic.Source}) {diagnostic.SourcePath}", diagnostic.Message));
            return exitCode;
        }
    }
}
