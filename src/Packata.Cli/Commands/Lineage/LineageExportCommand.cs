using System.Text;
using System.Text.Json;
using Packata.Core.Contracts;
using Packata.OpenLineage;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class LineageExportCommand : AsyncCommand<LineageExportSettings>
{
    private static readonly Uri Producer = new("https://github.com/Seddryck/Packata");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default) { WriteIndented = true };

    public override async Task<int> ExecuteAsync(CommandContext context, LineageExportSettings settings, CancellationToken cancellationToken)
    {
        if (!TryMapSourceDatasets(settings.SourceDatasets, out var sourceDatasets))
        {
            CommandOutput.Diagnostic(new("CLI_LINEAGE_OPTIONS", MappingSeverity.Error, "$",
                "Specify --source-dataset REFERENCE=NAME for each source, using each reference once."));
            return ExitCodes.Invocation;
        }

        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source, cancellationToken);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;

            var asset = ContractSelection.Asset(mapped.RequireValue(), settings.Asset);
            var result = OpenLineageExporter.Export(asset, new(
                new(settings.JobNamespace, settings.JobName),
                new(settings.DatasetNamespace, settings.OutputDataset),
                Producer,
                DateTimeOffset.UtcNow,
                input => sourceDatasets.TryGetValue(input.Reference, out var name) ? new(settings.DatasetNamespace, name) : null));
            foreach (var diagnostic in result.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!result.IsSuccessful) return ExitCodes.InvalidDocument;

            cancellationToken.ThrowIfCancellationRequested();
            var json = result.RequireValue().ToJsonString(JsonOptions) + Environment.NewLine;
            if (settings.Output is null) Console.Write(json);
            else ContractDocumentWriter.WriteBytes(settings.Output, Encoding.UTF8.GetBytes(json), cancellationToken);
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }

    private static bool TryMapSourceDatasets(string[] mappings, out Dictionary<string, string> sources)
    {
        sources = new(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var parts = mapping.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0 || !sources.TryAdd(parts[0], parts[1]))
                return false;
        }

        return sources.Count > 0;
    }
}
