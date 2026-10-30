using Packata.Application;
using Spectre.Console;

namespace Packata.Cli;

internal static class StandaloneInferenceOutput
{
    internal static ContractDocument Prepare(ContractDocument document)
    {
        var source = document.Source ?? throw new InvalidOperationException("The descriptor source is unavailable.");
        var result = StandaloneContractService.Prepare(ContractDocumentUpdater.Input(document), document.Contract, source.BaseUri, source.Archive is not null);
        foreach (var warning in result.Warnings)
            AnsiConsole.MarkupLine($"[yellow]CLI131:[/] {Markup.Escape(warning)}");
        return document with { OriginalText = result.Content };
    }
}