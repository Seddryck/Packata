namespace Packata.Core.Contracts;

public enum SatisfactionStatus { Satisfied, Unsatisfied, Unknown, Informational }
public sealed record SatisfactionPolicy(bool RejectAdditionalElements = false)
{
    public static SatisfactionPolicy Default { get; } = new();
    public string Id => RejectAdditionalElements ? "producer-consumer-strict-v1" : "producer-consumer-v1";
}

/// <summary>Existing canonical contract, plus optional native provenance. No consumer-specific schema.</summary>
public sealed record SatisfactionInput(DataContract? Contract, ContractStandard Standard = ContractStandard.Canonical,
    string? Source = null, IReadOnlyList<MappingDiagnostic>? Diagnostics = null,
    IReadOnlyDictionary<string, object?>? Descriptor = null);
public sealed record SatisfactionParticipant(string Role, string? Source, ContractIdentity? Identity, ContractStandard Standard);
public sealed record SatisfactionFinding(string Code, SatisfactionStatus Status, string ProducerPath,
    string ConsumerPath, object? Offered, object? Accepted, string Explanation);
public sealed record SatisfactionDiagnostic(string Role, string? Source, string Code, MappingSeverity Severity,
    string Path, string Message);

public sealed class ContractSatisfaction
{
    public int SchemaVersion => 1;
    public SatisfactionParticipant Producer { get; }
    public SatisfactionParticipant Consumer { get; }
    public string Policy { get; }
    public bool Completed { get; }
    public IReadOnlyList<SatisfactionFinding> Findings { get; }
    public IReadOnlyList<SatisfactionDiagnostic> Diagnostics { get; }
    public SatisfactionStatus Status => !Completed ? SatisfactionStatus.Unknown
        : Findings.Any(x => x.Status == SatisfactionStatus.Unsatisfied) ? SatisfactionStatus.Unsatisfied
        : Findings.Any(x => x.Status == SatisfactionStatus.Unknown) ? SatisfactionStatus.Unknown : SatisfactionStatus.Satisfied;
    public bool IsSatisfied => Completed && Findings.All(x => x.Status is SatisfactionStatus.Satisfied or SatisfactionStatus.Informational);

    internal ContractSatisfaction(SatisfactionInput producer, SatisfactionInput consumer, SatisfactionPolicy policy,
        bool completed, IEnumerable<SatisfactionFinding> findings, IEnumerable<SatisfactionDiagnostic> diagnostics)
    {
        Producer = new("producer", producer.Source, producer.Contract?.Identity, producer.Standard);
        Consumer = new("consumer", consumer.Source, consumer.Contract?.Identity, consumer.Standard);
        Policy = policy.Id;
        Completed = completed;
        Findings = Array.AsReadOnly(findings.OrderBy(x => x.ConsumerPath, StringComparer.Ordinal)
            .ThenBy(x => x.ProducerPath, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.OrderBy(x => x.Role, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Declaration-only producer/consumer comparison. Neither endpoints nor datasets are opened.</summary>
public static class ContractSatisfactionEvaluator
{
    public static ContractSatisfaction EvaluateSatisfaction(DataContract producer, DataContract consumer,
        SatisfactionPolicy policy, CancellationToken cancellationToken = default)
        => EvaluateSatisfaction(new SatisfactionInput(producer), new SatisfactionInput(consumer), policy, cancellationToken);

    public static ContractSatisfaction EvaluateSatisfaction(SatisfactionInput producer, SatisfactionInput consumer,
        SatisfactionPolicy policy, CancellationToken cancellationToken = default)
        => new SatisfactionAnalysis(producer, consumer, policy, cancellationToken).Run();
}
