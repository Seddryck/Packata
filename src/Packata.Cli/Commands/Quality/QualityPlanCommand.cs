using Packata.Application;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class QualityPlanCommand : AsyncCommand<QualityPlanSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, QualityPlanSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            var contract = QualityCommands.ValidateContract(source, cancellationToken);
            if (contract is null) return 2;
            var results = QualityExecutionService.Plan(contract, settings.Filters, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.OutputFormat == "json") WriteJson(results);
            else WriteText(results);
            return 0;
        }
        catch (QualitySelectionException error)
        {
            CommandOutput.Diagnostic(new("QUALITY002", MappingSeverity.Error, "$", error.Message));
            return 2;
        }
        catch (Exception error)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(error));
            return 2;
        }
    }

    private static void WriteJson(IReadOnlyList<QualityPlanResult> results) =>
        Console.Out.WriteLine(CommandOutput.RedactedJson(new
        {
            rules = results.Select(result => new
            {
                id = result.Rule.SourceId,
                path = result.Rule.SourcePath,
                target = result.Rule.Target,
                type = result.Rule.Kind,
                engine = result.EngineId,
                status = Status(result.Status),
                message = result.Message,
                diagnostics = result.Diagnostics.Select(finding => new { finding.Code, finding.Message })
            })
        }));

    private static void WriteText(IReadOnlyList<QualityPlanResult> results)
    {
        Console.Out.WriteLine($"Selected {results.Count} quality rule(s). No data was evaluated.");
        foreach (var result in results)
        {
            var rule = result.Rule;
            var identity = rule.SourceId ?? rule.SourcePath ?? "(anonymous)";
            var target = rule.Target is { } value ? string.Join('.', new[] { value.AssetId }.Concat(value.FieldPath)) : "(unknown target)";
            var engine = result.EngineId is { } id ? $", {id}" : "";
            Console.Out.WriteLine(CommandOutput.Redact($"  {Status(result.Status)}  {identity} on {target} ({rule.Kind}{engine})"));
            if (result.Message is { } message) Console.Out.WriteLine($"    {message}");
            foreach (var finding in result.Diagnostics)
                Console.Out.WriteLine(CommandOutput.Redact($"    {finding.Code}: {finding.Message}"));
        }
    }

    private static string Status(QualityPlanStatus status) => status switch
    {
        QualityPlanStatus.Planned => "planned",
        QualityPlanStatus.NeedsInput => "needs-input",
        QualityPlanStatus.Unsupported => "unsupported",
        _ => "invalid"
    };
}
