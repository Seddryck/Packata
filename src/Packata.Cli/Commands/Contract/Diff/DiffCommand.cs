using Packata.Core.Contracts;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DiffCommand : AsyncCommand<ContractComparisonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContractComparisonSettings settings, CancellationToken cancellationToken)
    {
        var diagnostics = new List<ComparisonDiagnostic>();
        var input = "baseline";
        var source = settings.Baseline;
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
            var diff = baseline.Diff(candidate);
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
                ? new("CLI021", MappingSeverity.Error, "$", "Invalid or unsupported ZIP descriptor package.")
                : CommandOutput.Failure(exception));
            WriteDiagnostics();
            return exception is InvalidDataException ? ExitCodes.InvalidDocument : CommandOutput.FailureExitCode(exception);
        }

        async Task<DataContract?> Load(string location, string format)
        {
            var document = await ContractSource.LoadAsync(location, format, cancellationToken).ConfigureAwait(false);
            var validation = ContractValidation.Validate(document);
            foreach (var diagnostic in validation) Add(diagnostic);
            if (validation.Any(x => x.Severity == MappingSeverity.Error)) return null;
            var mapped = document.Map();
            foreach (var diagnostic in mapped.Diagnostics) Add(diagnostic);
            if (mapped.Value is null && mapped.Diagnostics.All(x => x.Severity != MappingSeverity.Error))
                Add(new("CLI012", MappingSeverity.Error, "$", "The contract could not be mapped to the canonical model."));
            cancellationToken.ThrowIfCancellationRequested();
            return mapped.IsSuccessful ? mapped.Value : null;
        }

        void Add(MappingDiagnostic diagnostic) => diagnostics.Add(new(input, source,
            diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));

        void WriteDiagnostics()
        {
            foreach (var diagnostic in diagnostics)
                CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity,
                    $"{diagnostic.Input} ({diagnostic.Source}) {diagnostic.SourcePath}", diagnostic.Message));
        }

        int Invalid()
        {
            WriteDiagnostics();
            return ExitCodes.InvalidDocument;
        }
    }
}
