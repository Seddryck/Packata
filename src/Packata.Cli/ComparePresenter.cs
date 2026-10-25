using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;

namespace Packata.Cli;

internal static class ComparePresenter
{
    internal static JObject Result(string? baseline, string? candidate, CompatibilityResult? result,
        IReadOnlyList<DiffDiagnostic> diagnostics, string? failOn, int exitCode)
        => (JObject)CommandOutput.RedactedJson(new
        {
            schemaVersion = 1, baseline, candidate, direction = "baseline-to-candidate",
            policy = CompatibilityResult.ConsumerPolicy, completed = result?.Completed ?? false,
            outcome = Name(result?.Outcome ?? CompatibilityOutcome.Failed), isCompatible = result?.IsCompatible ?? false,
            hasChanges = result is { Completed: true } ? (bool?)result.Findings.Any(x => x.Change is not null || x.Rule == "unclassified-model-facet") : null,
            failOn, gatePassed = result is { Completed: true } ? (bool?)(exitCode == 0) : null, exitCode,
            totals = new
            {
                breaking = Count(result, CompatibilityClassification.Breaking),
                potentiallyBreaking = Count(result, CompatibilityClassification.PotentiallyBreaking),
                nonBreaking = Count(result, CompatibilityClassification.NonBreaking), unknown = Count(result, CompatibilityClassification.Unknown)
            },
            findings = (result?.Findings ?? []).Select(x => new
            {
                path = x.Path.Value, classification = Name(x.Classification), x.Rule, x.Explanation,
                change = x.Change is null ? null : DiffPresenter.Change(x.Change), sourceLocation = (object?)null
            }).ToArray(),
            diagnostics = diagnostics.Select(x => new
            {
                x.Input, x.Source, x.Code, x.Severity, x.SourcePath, x.Message, sourceLocation = (object?)null
            }).ToArray()
        });

    // Keep enum wire names independent from serializer configuration and .NET naming.
    private static string Name<T>(T value) where T : Enum => value.ToString() switch
    {
        "PotentiallyBreaking" => "potentiallyBreaking", "NonBreaking" => "nonBreaking",
        var name => char.ToLowerInvariant(name[0]) + name[1..]
    };

    private static int Count(CompatibilityResult? result, CompatibilityClassification classification)
        => result?.Findings.Count(x => x.Classification == classification) ?? 0;

    internal static void Text(JObject payload)
    {
        Console.WriteLine($"Baseline (previous): {payload["baseline"]}");
        Console.WriteLine($"Candidate (current): {payload["candidate"]}");
        Console.WriteLine($"Direction: baseline -> candidate; policy: {payload["policy"]}");
        Console.WriteLine($"Analysis: {payload["outcome"]}; completed: {payload["completed"]}; exit code: {payload["exitCode"]}");
        foreach (var finding in payload["findings"] ?? new JArray())
            Console.WriteLine($"{finding["classification"]} {finding["path"]}: {finding["explanation"]}");
        Console.WriteLine($"Totals: {payload["totals"]?.ToString(Newtonsoft.Json.Formatting.None)}");
    }
}
