using Packata.Core.Contracts;
using Packata.Application;
using PocketCsvReader;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class DialectInferCommand : AsyncCommand<DialectInferSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DialectInferSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ContractSource.IsArchive(settings.Source)
                || (Uri.TryCreate(settings.Source, UriKind.Absolute, out var uri) && !uri.IsFile))
                throw new NotSupportedException("Dialect inference requires an ordinary local ODCS or Data Package descriptor.");
            using var document = await ContractDocument.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var asset = InferenceService.SelectAsset(document.Contract, settings.Asset);
            var endpoint = InferenceService.SelectEndpoint(document.Contract, asset);
            var inference = await DialectInferenceService.InferAsync(InferenceService.Input(document), asset, endpoint,
                InferenceService.Options(settings.InferenceSettings()), cancellationToken).ConfigureAwait(false);
            Report(document, inference.Result, inference.SharedAssets.ToArray(), settings);
            if (!settings.Write && settings.Output is null) return 0;
            var update = inference.PrepareUpdate(ContractDocumentUpdater.Input(document), asset.Id, endpoint.Id,
                settings.AllowSharedEndpoint, settings.AcceptAmbiguous, cancellationToken);
            foreach (var warning in update.Warnings) Warning(warning);
            cancellationToken.ThrowIfCancellationRequested();
            var target = settings.Write ? document.SourcePath : Path.GetFullPath(settings.Output!);
            ContractDocumentWriter.Write(target, update.Content, overwrite: settings.Write);
            AnsiConsole.MarkupLine($"[green]Written:[/] {Markup.Escape(target)}");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Dialect inference was cancelled."); return 2; }
        catch (DialectUpdateException error)
        {
            var hint = error.Failure switch
            {
                DialectUpdateFailure.SharedEndpoint => " Use --allow-shared-endpoint to acknowledge the change.",
                DialectUpdateFailure.Ambiguous => " Specify --header or use --accept-ambiguous after reviewing candidates.",
                _ => string.Empty
            };
            await Console.Error.WriteLineAsync(InferencePresenter.SafeText(error.Message + hint)).ConfigureAwait(false);
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(InferencePresenter.SafeText(error.Message));
            return 1;
        }
    }

    private static void Report(ContractDocument document, CsvInferenceResult result, string[] shared, DialectInferSettings settings)
    {
        AnsiConsole.MarkupLine($"[bold]Inferred dialect for:[/] {Markup.Escape(settings.Asset)}");
        AnsiConsole.WriteLine($"Sampled records: {result.SampledRecordCount}; record limit: {settings.MaximumRecords}; source byte limit: {settings.MaximumBytes}.");
        AnsiConsole.WriteLine("Dialect detection scores at most 100 records; results describe a sample, not the complete dataset.");
        foreach (var diagnostic in document.MappingDiagnostics)
            Warning(diagnostic.Code + ": " + diagnostic.Message);
        foreach (var diagnostic in result.Diagnostics.Where(item => !item.Code.StartsWith("column.", StringComparison.Ordinal)
            && item.Code != "header.duplicate-name"))
        {
            var confidence = diagnostic.Confidence is { } value ? $" (confidence {value:P0})" : "";
            AnsiConsole.WriteLine(InferencePresenter.SafeText($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}{confidence}"));
        }
        Warning("Only delimiter, quote character, line terminator and header settings are candidates for update. Escape rules, null tokens, comments, spacing, encoding and other unsupported settings are retained, not inferred.");
        Warning("Quote candidates can tie when no quoted values are observed. Fewer than two records, truncated byte samples and inconsistent record widths cannot be saved.");
        if (shared.Length > 1) Warning("Shared endpoint affects assets: " + string.Join(", ", shared));
    }

    private static void Warning(string text) =>
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(InferencePresenter.SafeText(text))}");
}
