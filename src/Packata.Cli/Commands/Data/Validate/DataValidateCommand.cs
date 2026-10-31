using Packata.Application;
using Packata.Application.Validation;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DataValidateCommand : AsyncCommand<DataValidateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataValidateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken, retainArchive: true).ConfigureAwait(false);
            var (canRun, deferred) = ValidateDeclarations(source, settings.InputFormat, cancellationToken);
            if (!canRun) return ExitCodes.InvalidDocument;
            var mapped = ContractSelection.Map(source, cancellationToken);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var service = CreateService(source, mapped);
            var report = await service.ValidateAsync(mapped.RequireValue(), settings.Asset, cancellationToken: cancellationToken).ConfigureAwait(false);
            report = report with { CompilationFindings = report.CompilationFindings.Concat(deferred).ToArray() };
            await DataValidationPresenter.WriteAsync(report, settings.OutputFormat, Console.Out, cancellationToken).ConfigureAwait(false);
            return DataValidationPresenter.ExitCode(report);
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }

    private static (bool CanRun, CompilationFinding[] Deferred) ValidateDeclarations(ContractSource source, string format, CancellationToken token)
    {
        var validation = DescriptorValidation.Validate(source.OriginalText, format, ValidationLayers.Semantic, source.BaseUri, token);
        foreach (var diagnostic in validation.Diagnostics)
        {
            var severity = IsDeferredReference(diagnostic) ? MappingSeverity.Warning : diagnostic.Severity;
            CommandOutput.Diagnostic(new(diagnostic.Code, severity, diagnostic.SourcePath, diagnostic.Message));
        }
        var deferred = validation.Diagnostics.Where(IsDeferredReference).Select(diagnostic =>
            new CompilationFinding($"declaration:{diagnostic.SourcePath}", CompilationFindingKind.Unsupported,
                diagnostic.Code, diagnostic.Message)).ToArray();
        return (CanRunAvailableChecks(validation), deferred);
    }

    private static bool IsDeferredReference(ValidationFinding finding)
        => finding.Layer == ValidationLayers.Semantic && finding.Code == "SEM_EXTERNAL_REFERENCE";

    private static bool CanRunAvailableChecks(DescriptorValidationReport report)
        => report.Layers.Count > 0
            && report.Layers.All(layer => layer.Executed && layer.Prerequisites == ValidationPrerequisiteStatus.Satisfied)
            && report.Diagnostics.All(finding => finding.Severity != MappingSeverity.Error || IsDeferredReference(finding));

    private static DatasetValidationService CreateService(ContractSource source, MappingResult<DataContract> mapped)
    {
        IEndpointStreamResolver? streams = null;
        if (source.Archive is { } archive)
            streams = new PreviewService.ArchiveStreamResolver(archive,
                source.ArchiveDescriptor ?? throw new InvalidOperationException("The archive descriptor is unavailable."));
        var readers = PreviewService.CreateFactory(source.BaseUri, streams: streams);
        var document = new ContractDocument(source.Source, source.OriginalText, DocumentKind(source),
            source.NativeDocument, mapped.RequireValue(), mapped.Diagnostics) { Source = source };
        return new(readers, InferenceService.Input(document).Streams);
    }

    private static ContractDocumentKind DocumentKind(ContractSource source)
    {
        if (source.NativeDocument is Packata.OpenDataContract.DataContract) return ContractDocumentKind.OpenDataContractYaml;
        return source.OriginalText.TrimStart().StartsWith('{') ? ContractDocumentKind.DataPackageJson : ContractDocumentKind.DataPackageYaml;
    }
}
