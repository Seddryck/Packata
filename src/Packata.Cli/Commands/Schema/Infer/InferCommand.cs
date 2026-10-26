using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InferCommand : AsyncCommand<InferSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        InferSettings settings,
        CancellationToken cancellationToken)
    {
        var databaseOperation = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Write && (ContractSource.IsArchive(settings.Source)
                || (Uri.TryCreate(settings.Source, UriKind.Absolute, out var sourceUri) && !sourceUri.IsFile)))
                throw new NotSupportedException("--write requires an ordinary local descriptor; remote descriptors and archives cannot be replaced.");
            using var document = await ContractDocument.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var outputDocument = settings.Output is null ? document : StandaloneInferenceOutput.Prepare(document);
            CliDatabase.ResolveOverrides(document.Contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            var asset = InferenceService.SelectAsset(document.Contract, settings.Asset);
            var endpoint = InferenceService.SelectEndpoint(document.Contract, asset);
            databaseOperation = endpoint.Kind == EndpointKind.Database;
            var inference = endpoint.Kind switch
            {
                EndpointKind.File => await InferenceService.InferFileAsync(document, asset, endpoint, settings,
                    context.Arguments, cancellationToken).ConfigureAwait(false),
                EndpointKind.Database => await InferenceService.InferDatabaseAsync(
                    document, asset, endpoint, settings, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException(
                    $"Asset '{asset.Id}' is not bound to a supported file or database endpoint.")
            };

            InferencePresenter.Write(document.MappingDiagnostics, inference.Result,
                inference.Asset, inference.Endpoint);

            if (!inference.Result.IsSuccessful) return 1;

            if (settings.Write || settings.Output is not null)
            {
                var update = ContractDocumentUpdater.Update(
                    outputDocument, asset, endpoint, inference.Asset, inference.Endpoint);
                foreach (var warning in update.Warnings)
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
                var target = settings.Write ? document.SourcePath : Path.GetFullPath(settings.Output!);
                cancellationToken.ThrowIfCancellationRequested();
                ContractDocumentWriter.Write(target, update.Content, overwrite: settings.Write);
                AnsiConsole.MarkupLine($"[green]Written:[/] {Markup.Escape(target)}");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Inference was cancelled.[/]");
            return 2;
        }
        catch (Exception exception)
        {
            if (databaseOperation) return CliOutput.Error(exception);
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(InferencePresenter.SafeText(exception.Message))}");
            return 1;
        }
    }
}
