namespace Packata.Core.Contracts;

/// <summary>Declared derivation of an output field within an asset.</summary>
/// <param name="TargetPath">Field path using physical names when available.</param>
/// <param name="Sources">Declared source datasets and any resolved field dependencies.</param>
/// <param name="Logic">Original transformation expression.</param>
/// <param name="Description">Business description of the transformation.</param>
/// <param name="IsIdentity">Whether the output copies the input field unchanged.</param>
public sealed record FieldLineage(
    IReadOnlyList<string> TargetPath,
    IReadOnlyList<LineageSource> Sources,
    string? Logic = null,
    string? Description = null,
    bool IsIdentity = false);

/// <summary>A declared input dataset with optional resolved column dependencies.</summary>
/// <param name="Reference">Original source reference from the contract.</param>
/// <param name="AssetId">Canonical asset ID when the source is declared in the same contract.</param>
/// <param name="Fields">Resolved input field names, using physical names when available.</param>
public sealed record LineageSource(string Reference, string? AssetId, IReadOnlyList<string> Fields);
