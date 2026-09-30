namespace Packata.Core.Contracts;

public interface IDataContractMapper<in TDocument>
{
    MappingResult<DataContract> Map(TDocument document);
}

public enum MappingSeverity
{
    Information,
    Warning,
    Error
}

public sealed record MappingDiagnostic(
    string Code,
    MappingSeverity Severity,
    string SourcePath,
    string Message);

public sealed record MappingResult<T>(
    T? Value,
    IReadOnlyList<MappingDiagnostic> Diagnostics)
{
    public bool IsSuccessful =>
        Value is not null && Diagnostics.All(x => x.Severity != MappingSeverity.Error);

    public static MappingResult<T> Success(T value, params MappingDiagnostic[] diagnostics)
        => new(value, diagnostics);

    public static MappingResult<T> Failure(params MappingDiagnostic[] diagnostics)
        => new(default, diagnostics);
}

public sealed class ExtensionMetadata
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> _namespaces;

    public static ExtensionMetadata Empty { get; } = new();

    public ExtensionMetadata()
        : this(new Dictionary<string, IReadOnlyDictionary<string, object?>>())
    { }

    public ExtensionMetadata(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> namespaces)
        => _namespaces = new Dictionary<string, IReadOnlyDictionary<string, object?>>(namespaces);

    public IReadOnlyCollection<string> Namespaces => [.. _namespaces.Keys];

    public IReadOnlyDictionary<string, object?> this[string source] => _namespaces[source];

    public bool TryGetNamespace(
        string source,
        out IReadOnlyDictionary<string, object?> values)
        => _namespaces.TryGetValue(source, out values!);

    public static ExtensionMetadata For(
        string source,
        IReadOnlyDictionary<string, object?> values)
        => new(new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            [source] = new Dictionary<string, object?>(values)
        });
}
