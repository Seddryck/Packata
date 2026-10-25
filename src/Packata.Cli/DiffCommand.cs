using System.ComponentModel;
using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DiffSettings : CommandSettings
{
    [CommandArgument(0, "<BASELINE>")]
    [Description("Previous contract: local file, HTTP(S) descriptor, or ZIP package.")]
    public required string Baseline { get; init; }

    [CommandArgument(1, "<CANDIDATE>")]
    [Description("Current contract; additions are present only here, removals only in the baseline.")]
    public required string Candidate { get; init; }

    [CommandOption("--baseline-format <FORMAT>")]
    [Description("Baseline document standard: auto, odcs, or datapackage.")]
    [DefaultValue("auto")]
    public string BaselineFormat { get; init; } = "auto";

    [CommandOption("--candidate-format <FORMAT>")]
    [Description("Candidate document standard: auto, odcs, or datapackage.")]
    [DefaultValue("auto")]
    public string CandidateFormat { get; init; } = "auto";

    [CommandOption("--output <FORMAT>")]
    [Description("Result representation: text or json.")]
    [DefaultValue("text")]
    public string Output { get; init; } = "text";

    [CommandOption("--quiet")]
    [Description("Suppress results, including JSON; retain diagnostics on stderr.")]
    public bool Quiet { get; init; }

    public override ValidationResult Validate()
    {
        if (BaselineFormat is not ("auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--baseline-format must be auto, odcs, or datapackage.");
        if (CandidateFormat is not ("auto" or "odcs" or "datapackage"))
            return ValidationResult.Error("--candidate-format must be auto, odcs, or datapackage.");
        return Output is "text" or "json" ? ValidationResult.Success()
            : ValidationResult.Error("--output must be text or json.");
    }
}

internal sealed record DiffDiagnostic(string Input, string Source, string Code,
    MappingSeverity Severity, string SourcePath, string Message);

internal sealed class DiffCommand : AsyncCommand<DiffSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiffDiagnostic>();
        var input = "baseline";
        var source = settings.Baseline;
        try
        {
            var baseline = await Load(settings.Baseline, settings.BaselineFormat).ConfigureAwait(false);
            if (baseline is null) return Invalid();
            input = "candidate";
            source = settings.Candidate;
            var candidate = await Load(settings.Candidate, settings.CandidateFormat).ConfigureAwait(false);
            if (candidate is null) return Invalid();
            input = "comparison";
            source = settings.Baseline + " -> " + settings.Candidate;
            cancellationToken.ThrowIfCancellationRequested();
            var diff = baseline.Diff(candidate);
            var result = DiffPresenter.Result(settings.Baseline, settings.Candidate, diff, diagnostics);
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.Quiet)
            {
                if (settings.Output == "json") CommandOutput.Json(result);
                else DiffPresenter.Text(result);
            }
            if (settings.Quiet || settings.Output == "text") WriteDiagnostics();
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
            var validation = ValidateCommand.Validate(document);
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
