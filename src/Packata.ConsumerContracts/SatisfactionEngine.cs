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

    public SatisfactionStatus EvaluateParameter(IFieldRequirement required, DataField produced, string parameter) =>
        parameter switch
        {
            "length" => CompareBound(produced.Length, required.Length),
            "scale" => CompareBound(produced.Scale, required.Scale),
            "precision" => ComparePrecision(required, produced),
            _ => SatisfactionStatus.Unknown
        };

    private static SatisfactionStatus CompareBound(int? produced, int? required) => produced is null
        ? SatisfactionStatus.Unknown
        : produced <= required ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;

    private static SatisfactionStatus ComparePrecision(IFieldRequirement required, DataField produced)
    {
        var consumerCapacity = required.Precision - required.Scale;
        var producerCapacity = produced.Precision - produced.Scale;
        return consumerCapacity is null ? SatisfactionStatus.Unknown : CompareBound(producerCapacity, consumerCapacity);
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
            EvaluateDataset(producer, dataset, targetMatches, results);
        return new(results);
    }

    private void EvaluateDataset(DataContract producer, IDatasetRequirement dataset, bool targetMatches,
        List<RequirementSatisfaction> results)
    {
        var matches = producer.Assets.Where(x => x.Name == dataset.Name).ToArray();
        var asset = targetMatches && matches.Length == 1 ? matches[0] : null;
        var status = !targetMatches || matches.Length > 1 ? SatisfactionStatus.Unknown
            : asset is null ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Satisfied;
        results.Add(new(dataset.Name, null, "dataset", status,
            status == SatisfactionStatus.Satisfied ? "Dataset found" : "Dataset not found or cannot be resolved uniquely"));
        foreach (var field in dataset.Fields)
            EvaluateField(dataset.Name, asset, field, results);
    }

    private void EvaluateField(string dataset, DataAsset? asset, IFieldRequirement field,
        List<RequirementSatisfaction> results)
    {
        var (produced, status) = ResolveField(asset, field.Name);
        var message = status switch
        {
            SatisfactionStatus.Satisfied => "Field found",
            SatisfactionStatus.Unsatisfied => "Field not found",
            _ => "Field cannot be evaluated"
        };
        results.Add(new(dataset, field.Name, "field", status, message));
        var typeStatus = EvaluateType(dataset, field, produced, results);
        var canEvaluate = produced is not null && typeStatus == SatisfactionStatus.Satisfied;
        EvaluateNullability(dataset, field, canEvaluate ? produced : null, results);
        EvaluateParameters(dataset, field, canEvaluate ? produced : null, results);
    }

    private static (DataField? Field, SatisfactionStatus Status) ResolveField(DataAsset? asset, string name)
    {
        var fields = asset?.Schema?.Fields.Where(x => x.Name == name).ToArray();
        var produced = fields?.Length == 1 ? fields[0] : null;
        var status = asset?.Schema is null || fields?.Length > 1
            ? SatisfactionStatus.Unknown : produced is null ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Satisfied;
        return (produced, status);
    }

    private SatisfactionStatus EvaluateType(string dataset, IFieldRequirement field, DataField? produced,
        List<RequirementSatisfaction> results)
    {
        if (field.Type is null)
            return produced?.LogicalType is null ? SatisfactionStatus.Unknown : SatisfactionStatus.Satisfied;
        var status = produced is null ? SatisfactionStatus.Unknown : policy.Evaluate(field.Type, produced.LogicalType);
        results.Add(new(dataset, field.Name, "type", status,
            $"Expected {field.Type.Name}, found {produced?.LogicalType ?? "unknown"}"));
        return status;
    }

    private static void EvaluateNullability(string dataset, IFieldRequirement field, DataField? produced,
        List<RequirementSatisfaction> results)
    {
        if (field.Nullable is null) return;
        var status = produced?.Nullable is null ? SatisfactionStatus.Unknown
            : field.Nullable.Value || !produced.Nullable.Value ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;
        results.Add(new(dataset, field.Name, "nullable", status,
            status == SatisfactionStatus.Unknown ? "Nullability cannot be evaluated"
                : "Expected nullable: " + field.Nullable + ", found: " + produced!.Nullable));
    }

    private void EvaluateParameters(string dataset, IFieldRequirement field, DataField? produced,
        List<RequirementSatisfaction> results)
    {
        foreach (var parameter in new[] { ("length", field.Length), ("precision", field.Precision), ("scale", field.Scale) })
        {
            if (parameter.Item2 is null) continue;
            var status = produced is null ? SatisfactionStatus.Unknown : policy.EvaluateParameter(field, produced, parameter.Item1);
            results.Add(new(dataset, field.Name, parameter.Item1, status,
                status == SatisfactionStatus.Unknown ? parameter.Item1 + " cannot be evaluated"
                    : parameter.Item1 + " compatibility: " + status));
        }
    }
}
