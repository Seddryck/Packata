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

    public MappingResult<T> ReportDiagnostics(Action<MappingDiagnostic> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var diagnostic in Diagnostics)
            report(diagnostic);
        return this;
    }

    public MappingResult<T> ThrowOnErrors()
    {
        var errors = Diagnostics.Where(x => x.Severity == MappingSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new CanonicalMappingException(errors);
        return this;
    }

    public T RequireValue()
    {
        ThrowOnErrors();
        return Value ?? throw new CanonicalMappingException(Diagnostics);
    }
}

public sealed class CanonicalMappingException : InvalidOperationException
{
    public IReadOnlyList<MappingDiagnostic> Diagnostics { get; }

    public CanonicalMappingException(IReadOnlyList<MappingDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
        => Diagnostics = diagnostics;

    private static string BuildMessage(IReadOnlyList<MappingDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(x => x.Severity == MappingSeverity.Error).ToArray();
        if (errors.Length == 0)
            return "Canonical mapping did not produce a value.";
        return $"Canonical mapping failed: {string.Join("; ", errors.Select(x => $"{x.Code}: {x.Message}"))}";
    }
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
