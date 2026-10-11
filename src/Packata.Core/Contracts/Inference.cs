namespace Packata.Core.Contracts;

/// <summary>Severity of a decision or limitation reported while inferring a canonical contract.</summary>
public enum InferenceSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>A diagnostic produced while inferring a canonical contract.</summary>
public sealed record ContractInferenceDiagnostic(
    string Code,
    InferenceSeverity Severity,
    string SourcePath,
    string Message,
    double? Confidence = null);

/// <summary>The inferred canonical contract together with the decisions made while producing it.</summary>
public sealed record DataContractInferenceResult(
    DataContract Contract,
    IReadOnlyList<ContractInferenceDiagnostic> Diagnostics)
{
    public bool HasWarnings => Diagnostics.Any(x => x.Severity == InferenceSeverity.Warning);
}
