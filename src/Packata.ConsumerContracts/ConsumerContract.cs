namespace Packata.ConsumerContracts;

public interface IConsumerContract
{
    string Id { get; }
    string? Version { get; }
    ContractTarget? Target { get; }
    IReadOnlyList<IDatasetRequirement> Datasets { get; }
}

public sealed record ContractTarget(string Id);
public interface IDatasetRequirement
{
    string Name { get; }
    IReadOnlyList<IFieldRequirement> Fields { get; }
}
public interface IFieldRequirement
{
    string Name { get; }
    LogicalType? Type { get; }
}

public sealed record LogicalType(string Name)
{
    public static LogicalType String { get; } = new("string");
    public static LogicalType Boolean { get; } = new("boolean");
    public static LogicalType Integer { get; } = new("integer");
    public static LogicalType Long { get; } = new("long");
    public static LogicalType Decimal { get; } = new("decimal");
    public static LogicalType Float { get; } = new("float");
    public static LogicalType Double { get; } = new("double");
    public static LogicalType Date { get; } = new("date");
    public static LogicalType Time { get; } = new("time");
    public static LogicalType DateTime { get; } = new("datetime");
    public static LogicalType Duration { get; } = new("duration");
    public static LogicalType Binary { get; } = new("binary");
    public static LogicalType Object { get; } = new("object");
    public static LogicalType Array { get; } = new("array");
}

public sealed record FieldRequirement(string Name, LogicalType? Type = null) : IFieldRequirement;
public sealed record DatasetRequirement(string Name, IReadOnlyList<IFieldRequirement> Fields) : IDatasetRequirement;
public sealed record ConsumerContract(string Id, string? Version, ContractTarget? Target,
    IReadOnlyList<IDatasetRequirement> Datasets) : IConsumerContract
{
    public static ConsumerContractBuilder For(string id) => new(id);

    public void Validate()
    {
        RequireName(Id);
        if (Target is not null) RequireName(Target.Id);
        if (Datasets is null || Datasets.Count == 0)
            throw new ArgumentException("A consumer contract must declare at least one dataset.");
        var datasets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in Datasets)
        {
            RequireName(dataset.Name);
            if (!datasets.Add(dataset.Name)) throw new ArgumentException($"Duplicate dataset '{dataset.Name}'.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in dataset.Fields)
            {
                RequireName(field.Name);
                if (!fields.Add(field.Name)) throw new ArgumentException($"Duplicate field '{field.Name}'.");
                if (field.Type is not null) RequireName(field.Type.Name);
            }
        }
    }

    private static void RequireName(string name) => ArgumentException.ThrowIfNullOrWhiteSpace(name);
}

public sealed class ConsumerContractBuilder(string id)
{
    private string? version;
    private ContractTarget? target;
    private readonly List<IDatasetRequirement> datasets = [];
    public ConsumerContractBuilder Version(string value) { version = value; return this; }
    public ConsumerContractBuilder Target(string value) { target = new(value); return this; }
    public ConsumerContractBuilder Dataset(string name, Action<DatasetRequirementBuilder>? configure = null)
    {
        var builder = new DatasetRequirementBuilder(name);
        configure?.Invoke(builder);
        datasets.Add(builder.Build());
        return this;
    }
    public ConsumerContract Build()
    {
        var contract = new ConsumerContract(id, version, target, datasets.ToArray());
        contract.Validate();
        return contract;
    }
    public static implicit operator ConsumerContract(ConsumerContractBuilder builder) => builder.Build();
}

public sealed class DatasetRequirementBuilder(string name)
{
    private readonly List<IFieldRequirement> fields = [];
    public DatasetRequirementBuilder Field(string fieldName, Action<FieldRequirementBuilder>? configure = null)
    {
        var builder = new FieldRequirementBuilder(fieldName);
        configure?.Invoke(builder);
        fields.Add(builder.Build());
        return this;
    }
    internal DatasetRequirement Build() => new(name, fields.ToArray());
}
public sealed class FieldRequirementBuilder(string name)
{
    private LogicalType? type;
    public FieldRequirementBuilder Type(LogicalType value) { type = value; return this; }
    internal FieldRequirement Build() => new(name, type);
}
