using Packata.Core.Contracts;
using Packata.ResourceReaders;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class PreviewCommand : AsyncCommand<PreviewSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PreviewSettings settings, CancellationToken cancellationToken)
    {
        DataEndpoint? endpoint = null;
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken, retainArchive: true).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            CliDatabase.ResolveOverrides(mapped.Value!.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            var asset = ContractSelection.Asset(mapped.Value!, settings.Asset);
            var request = PreviewService.Request(mapped.Value!, asset);
            endpoint = request.Endpoint;
            using var database = endpoint.Kind == EndpointKind.Database
                ? new CliDatabase(new CliConfiguration(), source.BaseUri.IsFile ? source.BaseUri.LocalPath : string.Empty,
                    mapped.Value!.Endpoints, settings.Connectors, settings.ConnectionEnvironment) : null;
            var streams = source.Archive is null ? null
                : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
            var factory = database?.CreateReaders(source.BaseUri)
                ?? PreviewService.CreateFactory(source.BaseUri, streams: streams);
            var sample = await PreviewService.ReadAsync(factory, request,
                settings.Limit, settings.Columns, cancellationToken).ConfigureAwait(false);
            using var output = new OutputDestination(settings.Output);
            PreviewPresenter.Write(sample, settings.OutputFormat, output.Console, output.Writer);
            output.Commit();
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(PreviewFailure(exception, endpoint, settings.Asset));
            return CommandOutput.FailureExitCode(exception);
        }
    }

    private static MappingDiagnostic PreviewFailure(Exception exception, DataEndpoint? endpoint, string assetId)
    {
        var diagnostic = CommandOutput.Failure(exception);
        if (endpoint is not null && exception is not (OperationCanceledException or ContractOperationException))
            diagnostic = diagnostic with { Message = $"Unable to preview asset '{assetId}' at endpoint '{endpoint.Id}' (format '{endpoint.Format?.Name ?? "auto"}'). Check the reader provider, format, binding, and resource access." };
        return exception switch
        {
            ReaderProviderUnavailableException unavailable => diagnostic with
            { Message = $"{diagnostic.Message} Required package: {unavailable.SuggestedPackage}; registration: {unavailable.RegistrationMethod}." },
            Packata.Database.DatabaseConnectionException connector => diagnostic with
            { Code = connector.Code, Message = connector.Message + " Check 'packata connector list' and 'packata config list'." },
            CliException configuration => diagnostic with { Message = configuration.Message },
            _ => diagnostic
        };
    }
}
