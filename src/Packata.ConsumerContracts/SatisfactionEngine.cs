using Packata.Core.Contracts;

namespace Packata.ConsumerContracts;

public enum SatisfactionStatus { Satisfied, Unsatisfied, Unknown }
public sealed record RequirementSatisfaction(string? Dataset, string? Field, string Constraint,
    SatisfactionStatus Status, string Message);
public sealed record ContractSatisfaction(IReadOnlyList<RequirementSatisfaction> Results)
{
    public SatisfactionStatus Status => Results.Any(x => x.Status == SatisfactionStatus.Unsatisfied)
        ? SatisfactionStatus.Unsatisfied
        : Results.Any(x => x.Status == SatisfactionStatus.Unknown) ? SatisfactionStatus.Unknown : SatisfactionStatus.Satisfied;
    public bool IsSatisfied => Status == SatisfactionStatus.Satisfied;
}

public interface ILogicalTypeCompatibilityPolicy
{
    SatisfactionStatus Evaluate(LogicalType required, string? produced);
}
public sealed class ExactLogicalTypeCompatibilityPolicy : ILogicalTypeCompatibilityPolicy
{
    public SatisfactionStatus Evaluate(LogicalType required, string? produced) => produced is null
        ? SatisfactionStatus.Unknown
        : string.Equals(required.Name, produced, StringComparison.OrdinalIgnoreCase)
            ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;
}

public static class SatisfactionExtensions
{
    public static ContractSatisfaction Satisfies(this DataContract producer, IConsumerContract consumer,
        ILogicalTypeCompatibilityPolicy? policy = null) => new SatisfactionEngine(policy).Evaluate(producer, consumer);
}

public sealed class SatisfactionEngine(ILogicalTypeCompatibilityPolicy? policy = null)
{
    private readonly ILogicalTypeCompatibilityPolicy policy = policy ?? new ExactLogicalTypeCompatibilityPolicy();
    public ContractSatisfaction Evaluate(DataContract producer, IConsumerContract consumer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(consumer);
        new ConsumerContract(consumer.Id, consumer.Version, consumer.Target, consumer.Datasets).Validate();
        var results = new List<RequirementSatisfaction>();
        var targetMatches = consumer.Target is null || consumer.Target.Id == producer.Identity.Id;
        if (consumer.Target is not null)
            results.Add(new(null, null, "target", targetMatches ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied,
                targetMatches ? "Target matches" : $"Expected contract '{consumer.Target.Id}', found '{producer.Identity.Id}'"));
        foreach (var dataset in consumer.Datasets)
        {
            var matches = producer.Assets.Where(x => x.Name == dataset.Name).ToArray();
            var asset = targetMatches && matches.Length == 1 ? matches[0] : null;
            var datasetStatus = !targetMatches || matches.Length > 1 ? SatisfactionStatus.Unknown
                : asset is null ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Satisfied;
            results.Add(new(dataset.Name, null, "dataset", datasetStatus,
                datasetStatus == SatisfactionStatus.Satisfied ? "Dataset found" : "Dataset not found or cannot be resolved uniquely"));
            foreach (var field in dataset.Fields)
            {
                var fields = asset?.Schema?.Fields.Where(x => x.Name == field.Name).ToArray();
                var produced = fields?.Length == 1 ? fields[0] : null;
                var status = asset is null || asset.Schema is null || fields?.Length > 1
                    ? SatisfactionStatus.Unknown : produced is null ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Satisfied;
                results.Add(new(dataset.Name, field.Name, "field", status,
                    status == SatisfactionStatus.Satisfied ? "Field found" : status == SatisfactionStatus.Unsatisfied ? "Field not found" : "Field cannot be evaluated"));
                if (field.Type is not null)
                {
                    var typeStatus = produced is null ? SatisfactionStatus.Unknown : policy.Evaluate(field.Type, produced.LogicalType);
                    results.Add(new(dataset.Name, field.Name, "type", typeStatus,
                        $"Expected {field.Type.Name}, found {produced?.LogicalType ?? "unknown"}"));
                }
            }
        }
        return new(results);
    }
}
