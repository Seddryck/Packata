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
    SatisfactionStatus EvaluateParameter(IFieldRequirement required, DataField produced, string parameter);
}
public sealed class ExactLogicalTypeCompatibilityPolicy : ILogicalTypeCompatibilityPolicy
{
    public SatisfactionStatus Evaluate(LogicalType required, string? produced) => produced is null
        ? SatisfactionStatus.Unknown
        : string.Equals(Normalize(required.Name), Normalize(produced), StringComparison.OrdinalIgnoreCase)
            ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;

    private static string Normalize(string name) => name.ToLowerInvariant() switch
    {
        "number" => "decimal", "timestamp" => "datetime", _ => name
    };

    public SatisfactionStatus EvaluateParameter(IFieldRequirement required, DataField produced, string parameter)
    {
        bool? compatible = parameter switch
        {
            "length" when produced.Length is not null => produced.Length <= required.Length,
            "scale" when produced.Scale is not null => produced.Scale <= required.Scale,
            "precision" when produced.Precision is not null && produced.Scale is not null
                && required.Precision is not null && required.Scale is not null =>
                produced.Precision - produced.Scale <= required.Precision - required.Scale,
            _ => null
        };
        return compatible is null ? SatisfactionStatus.Unknown
            : compatible.Value ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;
    }
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
                var typeStatus = produced?.LogicalType is null ? SatisfactionStatus.Unknown : SatisfactionStatus.Satisfied;
                if (field.Type is not null)
                {
                    typeStatus = produced is null ? SatisfactionStatus.Unknown : policy.Evaluate(field.Type, produced.LogicalType);
                    results.Add(new(dataset.Name, field.Name, "type", typeStatus,
                        $"Expected {field.Type.Name}, found {produced?.LogicalType ?? "unknown"}"));
                }
                var canEvaluate = produced is not null && typeStatus == SatisfactionStatus.Satisfied;
                if (field.Nullable is not null)
                {
                    var nullableStatus = !canEvaluate || produced!.Nullable is null ? SatisfactionStatus.Unknown
                        : field.Nullable.Value || !produced.Nullable.Value ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;
                    results.Add(new(dataset.Name, field.Name, "nullable", nullableStatus,
                        nullableStatus == SatisfactionStatus.Unknown ? "Nullability cannot be evaluated"
                            : "Expected nullable: " + field.Nullable + ", found: " + produced!.Nullable));
                }
                foreach (var parameter in new[] { ("length", field.Length), ("precision", field.Precision), ("scale", field.Scale) })
                {
                    if (parameter.Item2 is null) continue;
                    var parameterStatus = canEvaluate ? policy.EvaluateParameter(field, produced!, parameter.Item1) : SatisfactionStatus.Unknown;
                    results.Add(new(dataset.Name, field.Name, parameter.Item1, parameterStatus,
                        parameterStatus == SatisfactionStatus.Unknown ? parameter.Item1 + " cannot be evaluated"
                            : parameter.Item1 + " compatibility: " + parameterStatus));
                }
            }
        }
        return new(results);
    }
}

