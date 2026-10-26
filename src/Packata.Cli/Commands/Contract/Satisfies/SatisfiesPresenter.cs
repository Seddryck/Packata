using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;

namespace Packata.Cli;

internal static class SatisfiesPresenter
{
    internal static JObject Result(string? producer, string? consumer, ContractSatisfaction? result,
        IReadOnlyList<SatisfactionDiagnostic> diagnostics, string unknown, int exitCode)
        => (JObject)CommandOutput.RedactedJson(new
        {
            schemaVersion = 1,
            producer = result?.Producer ?? new SatisfactionParticipant("producer", producer, null, ContractStandard.Canonical),
            consumer = result?.Consumer ?? new SatisfactionParticipant("consumer", consumer, null, ContractStandard.Canonical),
            policy = result?.Policy ?? SatisfactionPolicy.Default.Id,
            completed = result?.Completed ?? false,
            status = result?.Status ?? SatisfactionStatus.Unknown,
            isSatisfied = result?.IsSatisfied ?? false,
            unknownPolicy = unknown, gatePassed = exitCode == 0, exitCode,
            findings = result?.Findings ?? [], diagnostics
        });

    internal static void Text(JObject payload, TextWriter writer)
    {
        writer.WriteLine($"Producer: {payload["producer"]?["source"]} ({payload["producer"]?["identity"]?["id"]})");
        writer.WriteLine($"Consumer: {payload["consumer"]?["source"]} ({payload["consumer"]?["identity"]?["id"]})");
        writer.WriteLine($"Policy: {payload["policy"]}; completed: {payload["completed"]}; status: {payload["status"]}");
        writer.WriteLine($"Proven satisfaction: {payload["isSatisfied"]}; gate passed: {payload["gatePassed"]}; Unknown policy: {payload["unknownPolicy"]}");
        foreach (var finding in payload["findings"] ?? new JArray())
            writer.WriteLine($"{finding["status"]} {finding["code"]} {finding["consumerPath"]}: {finding["explanation"]} Offered={finding["offered"]}; Accepted={finding["accepted"]}");
    }
}
