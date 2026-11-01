using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Packata.Core.Contracts;

public enum QualityInputScope { Value,
    Row,
    Dataset,
    Observation }
public enum QualityProgramKind { Expression,
    Predicate }
public enum ExpectationOperator { Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Between,
    NotBetween }
public enum QualityValueKind { Scalar,
    Timestamp,
    Date,
    Time,
    Duration,
    List,
    Object }

/// <summary>Detached typed argument/operand. Scalar membership uses the same ScalarValue as value domains.</summary>
public sealed record QualityValue
{
    public QualityValueKind Kind { get; }
    public object? Value { get; }
    private QualityValue(QualityValueKind kind, object? value) => (Kind, Value) = (kind, value);
    public static QualityValue From(object? value) => value switch
    {
        QualityValue typed => typed,
        DateTimeOffset timestamp => new(QualityValueKind.Timestamp, timestamp),
        DateTime date when date.Kind == DateTimeKind.Utc => new(QualityValueKind.Timestamp, new DateTimeOffset(date)),
        DateTime => throw new ArgumentException("Timestamps require an explicit offset or UTC kind."),
        DateOnly date => new(QualityValueKind.Date, date),
        TimeOnly time => new(QualityValueKind.Time, time),
        ContractDuration duration => new(QualityValueKind.Duration, duration),
        TimeSpan duration => new(QualityValueKind.Duration, new ContractDuration(0, duration)),
        JsonElement element when element.ValueKind == JsonValueKind.Array => From(element.EnumerateArray().Select(item => From(item)).ToArray()),
        JsonElement element when element.ValueKind == JsonValueKind.Object => From(element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value)),
        IDictionary dictionary => new(QualityValueKind.Object, new ReadOnlyDictionary<string, QualityValue>(dictionary.Keys.Cast<object>()
            .ToDictionary(key => key as string ?? throw new ArgumentException("Argument keys must be strings."), key => From(dictionary[key]), StringComparer.Ordinal))),
        IEnumerable sequence when value is not string => new(QualityValueKind.List, Array.AsReadOnly(sequence.Cast<object?>().Select(From).ToArray())),
        _ => new(QualityValueKind.Scalar, ScalarValue.From(value))
    };

    public object? ToObject() => Kind switch
    {
        QualityValueKind.Scalar => ((ScalarValue)Value!).Value,
        QualityValueKind.List => ((IReadOnlyList<QualityValue>)Value!).Select(v => v.ToObject()).ToArray(),
        QualityValueKind.Object => ((IReadOnlyDictionary<string, QualityValue>)Value!).ToDictionary(p => p.Key, p => p.Value.ToObject()),
        _ => Value
    };
}

public sealed record QualityExpectation(ExpectationOperator Operator, QualityValue Operand, QualityValue? Upper = null);
public sealed record QualityTarget
{
    public string AssetId { get; }
    public IReadOnlyList<string> FieldPath { get; }
    public QualityTarget(string assetId, IEnumerable<string>? fieldPath = null)
        => (AssetId, FieldPath) = (assetId, Array.AsReadOnly((fieldPath ?? []).ToArray()));
}

public interface IQualityDefinition;
public sealed record LibraryQuality : IQualityDefinition
{
    public string Catalog { get; }
    public string Version { get; }
    public string Metric { get; }
    public string Unit { get; }
    public IReadOnlyDictionary<string, QualityValue> Arguments { get; }
    public LibraryQuality(string catalog, string version, string metric,
        IReadOnlyDictionary<string, QualityValue>? arguments = null, string unit = "rows")
        => (Catalog, Version, Metric, Unit, Arguments) = (catalog, version, metric, unit,
            new ReadOnlyDictionary<string, QualityValue>((arguments ?? new Dictionary<string, QualityValue>()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
}
public sealed record SqlQuality(string Query, string? Unit = null) : IQualityDefinition;
public sealed record ExpressifQuality(string Program, QualityProgramKind ProgramKind, QualityInputScope Scope,
    string PayloadVersion = "1", string? OriginalPayload = null,
    IReadOnlyDictionary<string, QualityValue>? Bindings = null) : IQualityDefinition
{
    public IReadOnlyDictionary<string, QualityValue> Bindings { get; init; } =
        new ReadOnlyDictionary<string, QualityValue>((Bindings ?? new Dictionary<string, QualityValue>()).ToDictionary(p => p.Key, p => p.Value));
}
public sealed record TextQuality(string? Text) : IQualityDefinition;
public sealed record CustomQuality(string? Engine, string? Implementation) : IQualityDefinition;

public sealed record QualityMetadata(string? Name = null, string? Description = null, string? Dimension = null,
    string? BusinessImpact = null, string? Method = null, string? Severity = null,
    string? Scheduler = null, string? Schedule = null,
    IReadOnlyList<string>? Tags = null, IReadOnlyList<AuthoritativeReference>? References = null)
{
    public IReadOnlyList<string> Tags { get; init; } = Array.AsReadOnly((Tags ?? []).ToArray());
    public IReadOnlyList<AuthoritativeReference> References { get; init; } = Array.AsReadOnly((References ?? []).ToArray());
}

/// <summary>Declarative quality, independent of an execution backend. Legacy constructor remains available.</summary>
public sealed record DataQualityRule(string Kind, string? Description = null, string? Expression = null)
{
    public string? SourceId { get; init; }
    public string? SourcePath { get; init; }
    public string ExecutionId => SourceId ?? SourcePath ?? $"{Kind}:{Expression}";
    public QualityTarget? Target { get; init; }
    public IQualityDefinition? Definition { get; init; }
    public IReadOnlyList<QualityExpectation> Expectations { get; init; } = [];
    public QualityMetadata Metadata { get; init; } = new();
    public ExtensionMetadata Extensions { get; init; } = ExtensionMetadata.Empty;
    public IReadOnlyList<MappingDiagnostic> Diagnostics { get; init; } = [];

    public DataQualityRule Snapshot() => this with
    {
        Expectations = Array.AsReadOnly(Expectations.ToArray()),
        Metadata = Metadata with { Tags = Array.AsReadOnly(Metadata.Tags.ToArray()), References = Array.AsReadOnly(Metadata.References.ToArray()) },
        Extensions = MetadataSnapshot.CopyExtensions(Extensions),
        Diagnostics = Array.AsReadOnly(Diagnostics.ToArray())
    };
}
