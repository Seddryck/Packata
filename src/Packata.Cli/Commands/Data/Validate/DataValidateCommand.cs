using Packata.Application;
using Packata.Application.Validation;
using Packata.Core.Contracts;
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
            if (!ValidateDeclarations(source, settings.InputFormat, cancellationToken)) return ExitCodes.InvalidDocument;
            var mapped = ContractSelection.Map(source, cancellationToken);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var service = CreateService(source, mapped);
            var report = await service.ValidateAsync(mapped.RequireValue(), settings.Asset, cancellationToken: cancellationToken).ConfigureAwait(false);
            await DataValidationPresenter.WriteAsync(report, settings.OutputFormat, Console.Out, cancellationToken).ConfigureAwait(false);
            return DataValidationPresenter.ExitCode(report);
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }

    private static bool ValidateDeclarations(ContractSource source, string format, CancellationToken token)
    {
        var validation = DescriptorValidation.Validate(source.OriginalText, format, ValidationLayers.Semantic, source.BaseUri, token);
        foreach (var diagnostic in validation.Diagnostics)
            CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
        return validation.Valid;
    }

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
