namespace Packata.Core.Contracts;

public enum ContractStandard { Canonical, Odcs, DataPackage }
public enum CompatibilityClassification { Breaking, PotentiallyBreaking, NonBreaking, Unknown }
public enum CompatibilityOutcome { Compatible, Breaking, PotentiallyBreaking, Unknown, Failed }

/// <summary>Input provenance and mapping diagnostics travel with the canonical model.
/// Descriptor is the original parsed JSON/YAML tree (string keys, lists, and scalar values).
/// Supply it for native standards so members ignored by native parsers can be diagnosed.</summary>
public sealed record CompatibilityInput(DataContract? Contract, ContractStandard Standard = ContractStandard.Canonical,
    IReadOnlyList<MappingDiagnostic>? Diagnostics = null, IReadOnlyDictionary<string, object?>? Descriptor = null);

public sealed record CompatibilityDiagnostic(string Input, string Code, MappingSeverity Severity, string Path, string Message);

/// <summary>One semantic facet of a typed factual change. Change is null for a source
/// coverage finding or a model facet not represented by the factual differ.</summary>
public sealed record CompatibilityFinding(ContractPath Path, CompatibilityClassification Classification,
    string Rule, string Explanation, IContractChange? Change);

public sealed class CompatibilityResult
{
    public const string ConsumerPolicy = "baseline-consumers-v1";
    public string Direction => "baseline-to-candidate";
    public string Policy => ConsumerPolicy;
    public bool Completed { get; }
    public CompatibilityOutcome Outcome { get; }
    public bool IsCompatible => Completed && Outcome == CompatibilityOutcome.Compatible;
    public IReadOnlyList<CompatibilityFinding> Findings { get; }
    public IReadOnlyList<CompatibilityDiagnostic> Diagnostics { get; }

    internal CompatibilityResult(bool completed, IEnumerable<CompatibilityFinding> findings,
        IEnumerable<CompatibilityDiagnostic> diagnostics)
    {
        Completed = completed;
        Findings = Array.AsReadOnly(findings.OrderBy(x => x.Path.Value, StringComparer.Ordinal)
            .ThenBy(x => x.Rule, StringComparer.Ordinal).ThenBy(x => x.Change?.GetType().Name, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.OrderBy(x => x.Input, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToArray());
        Outcome = !completed ? CompatibilityOutcome.Failed
            : Findings.Any(x => x.Classification == CompatibilityClassification.Breaking) ? CompatibilityOutcome.Breaking
            : Findings.Any(x => x.Classification == CompatibilityClassification.PotentiallyBreaking) ? CompatibilityOutcome.PotentiallyBreaking
            : Findings.Any(x => x.Classification == CompatibilityClassification.Unknown) ? CompatibilityOutcome.Unknown
            : CompatibilityOutcome.Compatible;
    }
}

/// <summary>Can consumers built against the baseline safely consume candidate data?
/// The versioned policy assumes name-based access and tolerance of additional datasets/fields.
/// Stronger producer guarantees are safe; weaker guarantees can break baseline consumers.</summary>
public static class ContractCompatibility
{
    public static CompatibilityResult Compare(DataContract baseline, DataContract candidate,
        CancellationToken cancellationToken = default)
        => Compare(new CompatibilityInput(baseline), new CompatibilityInput(candidate), cancellationToken);

    public static CompatibilityResult Compare(CompatibilityInput baseline, CompatibilityInput candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var diagnostics = new List<CompatibilityDiagnostic>();
        var findings = new List<CompatibilityFinding>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(baseline.Standard) || !Enum.IsDefined(candidate.Standard) || baseline.Standard != candidate.Standard)
                diagnostics.Add(new("comparison", "CMP001", MappingSeverity.Error, "$", "Only same-standard comparisons are supported."));
            Inspect(baseline, "baseline");
            Inspect(candidate, "candidate");
            if (diagnostics.Any(x => x.Severity == MappingSeverity.Error)) return new(false, findings, diagnostics);
            var previous = Prepare(baseline);
            var current = Prepare(candidate);
            foreach (var change in previous.Diff(current).Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                findings.AddRange(CompatibilityRules.Classify(change));
            }
            // These newer canonical facets are not emitted by ContractDiffer yet.
            Extra("sla", previous.Sla, current.Sla);
            Extra("lifecycle", previous.Lifecycle, current.Lifecycle);
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, findings, diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new("comparison", "CMP130", MappingSeverity.Error, "$", "Compatibility analysis was cancelled."));
            return new(false, findings, diagnostics);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            // Avoid exposing source values or exception messages containing credentials.
            diagnostics.Add(new("comparison", "CMP002", MappingSeverity.Error, "$", "Invalid semantic identifiers or unsupported canonical values prevented analysis."));
            return new(false, findings, diagnostics);
        }

        void Inspect(CompatibilityInput input, string name)
        {
            if (input.Contract is null)
                diagnostics.Add(new(name, "CMP003", MappingSeverity.Error, "$", "No canonical contract was supplied."));
            foreach (var diagnostic in input.Diagnostics ?? [])
            {
                diagnostics.Add(new(name, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
                if (diagnostic.Severity == MappingSeverity.Warning)
                    Unknown(diagnostic.SourcePath, "mapping-warning", "Mapping reported ambiguity or unsupported semantics; compatibility is not established.");
            }
            if (input.Standard != ContractStandard.Canonical && input.Descriptor is null)
                Unknown("$", "source-coverage", "The original descriptor was not supplied; source mapping coverage cannot be established.");
            if (input.Descriptor is { } descriptor)
                foreach (var path in CompatibilityCoverage.Unsupported(descriptor, input.Standard))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diagnostics.Add(new(name, "CMP020", MappingSeverity.Warning, path, "Source semantics are not covered by this compatibility policy."));
                    Unknown(path, "source-coverage", "Unsupported or unmapped source semantics prevent a complete compatibility verdict.");
                }
            if (input.Contract is { } contract)
                foreach (var asset in contract.Assets)
                {
                    if (asset.Schema is null)
                        Unknown(ContractPath.Root("schema").Append(asset.Id).Value, "schema-coverage", "No schema is available for this dataset.");
                    foreach (var field in asset.Schema?.Fields ?? []) CheckField(field, ContractPath.Root("schema").Append(asset.Id).Append("properties").Append(field.Name));
                }

            void CheckField(DataField field, ContractPath path)
            {
                if (field.Constraints.Any(x => string.IsNullOrEmpty(x.Kind))
                    || field.Constraints.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() != field.Constraints.Count)
                    diagnostics.Add(new(name, "CMP004", MappingSeverity.Error, path.Value, "Constraint kinds must be non-empty and unique."));
                if (!CompatibilityRules.KnownType(field.LogicalType))
                    Unknown(path.Append("type").Value, "type-coverage", "The field type is unspecified or unsupported by this policy.");
                foreach (var constraint in field.Constraints.Where(x => !CompatibilityRules.SupportedConstraintValue(x)))
                    Unknown(path.Append("constraints").Append(constraint.Kind).Value, "constraint-coverage", "This constraint has no supported compatibility rule.");
                foreach (var child in field.Children) CheckField(child, path.Append("properties").Append(child.Name));
                if (field.Items is { } items) CheckField(items, path.Append("items"));
            }

            void Unknown(string path, string rule, string explanation)
                => findings.Add(new(new(path), CompatibilityClassification.Unknown, name + ":" + rule, explanation, null));
        }

        void Extra(string path, object? previous, object? current)
        {
            if (!ContractDiffValues.Equal(previous, current))
                findings.Add(new(ContractPath.Root(path), CompatibilityClassification.Unknown, "unclassified-model-facet",
                    "This canonical facet changed but is not covered by the factual differ or compatibility policy.", null));
        }
    }

    private static DataContract Prepare(CompatibilityInput input)
    {
        var contract = input.Contract!;
        if (input.Standard != ContractStandard.DataPackage) return contract;
        // DP required rejects null/missing values. ODCS required and nullable are independent.
        return contract with { Assets = contract.Assets.Select(asset => asset with
        {
            Schema = asset.Schema is { } schema ? schema with { Fields = schema.Fields.Select(Field).ToArray() } : null
        }).ToArray() };

        static DataField Field(DataField field) => field with
        {
            Nullable = !field.Required, Children = field.Children.Select(Field).ToArray(),
            Items = field.Items is { } items ? Field(items) : null
        };
    }
}
