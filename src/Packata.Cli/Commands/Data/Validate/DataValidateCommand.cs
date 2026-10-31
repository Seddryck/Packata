using System.ComponentModel;
using Packata.Application;
using Packata.Core.Execution;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DataValidateSettings : ContractSourceSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Asset to validate; default is all declared assets.")]
    public string? Asset { get; init; }

    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: text or json.")]
    [DefaultValue("text")]
    public string OutputFormat { get; init; } = "text";

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        if (Asset is not null && string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset must be non-empty.");
        return OutputFormat is "text" or "json" ? ValidationResult.Success()
            : ValidationResult.Error("--output-format must be text or json.");
    }
}

internal sealed class DataValidateCommand : AsyncCommand<DataValidateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataValidateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken, retainArchive: true).ConfigureAwait(false);
            var validation = Packata.Application.Validation.DescriptorValidation.Validate(source.OriginalText,
                settings.InputFormat, Packata.Application.Validation.ValidationLayers.Semantic, source.BaseUri, cancellationToken);
            foreach (var diagnostic in validation.Diagnostics)
                CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            if (!validation.Valid) return ExitCodes.InvalidDocument;
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var streams = source.Archive is null ? null : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
            var readers = PreviewService.CreateFactory(source.BaseUri, streams: streams);
            var document = new ContractDocument(source.Source, source.OriginalText, ContractDocumentKind.OpenDataContractYaml,
                source.NativeDocument, mapped.RequireValue(), mapped.Diagnostics) { Source = source };
            var report = await new DatasetValidationService(readers, InferenceService.Input(document).Streams).ValidateAsync(mapped.RequireValue(), settings.Asset,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (settings.OutputFormat == "json")
                Console.Out.WriteLine(ContractJson.RedactedJson(report).ToString());
            else
            {
                foreach (var result in report.Results)
                {
                    Console.Out.WriteLine($"{result.Outcome}: {CommandOutput.Redact(result.Descriptor.ExecutionId)} [{CommandOutput.Redact(result.Descriptor.SourcePath ?? "")}]");
                    foreach (var evidence in result.Measurement.Evidence)
                        Console.Out.WriteLine($"  {evidence.Name}: {ContractJson.RedactedJson(evidence.Value.ToObject() ?? "null").ToString(Newtonsoft.Json.Formatting.None)}");
                    foreach (var diagnostic in result.Measurement.Diagnostics) Console.Out.WriteLine($"  {diagnostic.Code}: {diagnostic.Message}");
                }
                foreach (var finding in report.CompilationFindings) Console.Out.WriteLine($"{finding.Kind}: {finding.Code}: {finding.Message}");
                Console.Out.WriteLine($"Structural validation: {(report.Passed ? "passed" : report.IsComplete ? "failed" : "incomplete")}.");
            }
            return report.Passed ? 0 : report.Results.Any(r => r.Outcome == RuleOutcome.Error) ? 3 : report.IsComplete ? 1 : 4;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
