using Packata.Application;
using Packata.Core.Execution;

namespace Packata.Cli;

internal static class DataValidationPresenter
{
    internal static async Task WriteAsync(RuleEvaluationReport report, string format, TextWriter output, CancellationToken token)
    {
        if (format == "json")
        {
            await output.WriteLineAsync(ContractJson.RedactedJson(report).ToString().AsMemory(), token).ConfigureAwait(false);
            return;
        }
        foreach (var result in report.Results)
            await WriteResultAsync(result, output, token).ConfigureAwait(false);
        foreach (var finding in report.CompilationFindings)
            await output.WriteLineAsync($"{finding.Kind}: {finding.Code}: {finding.Message}".AsMemory(), token).ConfigureAwait(false);
        await output.WriteLineAsync($"Structural validation: {Summary(report)}.".AsMemory(), token).ConfigureAwait(false);
    }

    private static async Task WriteResultAsync(RuleResult result, TextWriter output, CancellationToken token)
    {
        var identity = CommandOutput.Redact(result.Descriptor.ExecutionId);
        var source = CommandOutput.Redact(result.Descriptor.SourcePath ?? "");
        await output.WriteLineAsync($"{result.Outcome}: {identity} [{source}]".AsMemory(), token).ConfigureAwait(false);
        foreach (var evidence in result.Measurement.Evidence)
        {
            var value = ContractJson.RedactedJson(evidence.Value.ToObject() ?? "null").ToString(Newtonsoft.Json.Formatting.None);
            await output.WriteLineAsync($"  {evidence.Name}: {value}".AsMemory(), token).ConfigureAwait(false);
        }
        foreach (var diagnostic in result.Measurement.Diagnostics)
            await output.WriteLineAsync($"  {diagnostic.Code}: {diagnostic.Message}".AsMemory(), token).ConfigureAwait(false);
    }

    private static string Summary(RuleEvaluationReport report)
    {
        if (report.Passed) return "passed";
        return report.IsComplete ? "failed" : "incomplete";
    }

    internal static int ExitCode(RuleEvaluationReport report)
    {
        if (report.Passed) return 0;
        if (report.Results.Any(r => r.Outcome == RuleOutcome.Error)) return 3;
        return report.IsComplete ? 1 : 4;
    }
}
