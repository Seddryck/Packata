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
    public string Direction { get; } = "baseline-to-candidate";
    public string Policy { get; } = ConsumerPolicy;
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
        Outcome = DetermineOutcome();
    }

    private CompatibilityOutcome DetermineOutcome()
    {
        if (!Completed) return CompatibilityOutcome.Failed;
        if (Findings.Any(x => x.Classification == CompatibilityClassification.Breaking)) return CompatibilityOutcome.Breaking;
        if (Findings.Any(x => x.Classification == CompatibilityClassification.PotentiallyBreaking)) return CompatibilityOutcome.PotentiallyBreaking;
        if (Findings.Any(x => x.Classification == CompatibilityClassification.Unknown)) return CompatibilityOutcome.Unknown;
        return CompatibilityOutcome.Compatible;
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
            var inspection = new CompatibilityInspection(diagnostics, findings, cancellationToken);
            inspection.Inspect(baseline, "baseline");
            inspection.Inspect(candidate, "candidate");
            if (diagnostics.Any(x => x.Severity == MappingSeverity.Error)) return new(false, findings, diagnostics);
            var previous = Prepare(baseline);
            var current = Prepare(candidate);
            foreach (var change in previous.Diff(current).Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                findings.AddRange(CompatibilityRules.Classify(change));
            }
            // These newer canonical facets are not emitted by ContractDiffer yet.
            Extra(findings, "sla", previous.Sla, current.Sla);
            Extra(findings, "lifecycle", previous.Lifecycle, current.Lifecycle);
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
    }

    private static void Extra(List<CompatibilityFinding> findings, string path, object? previous, object? current)
    {
        if (!ContractDiffValues.Equal(previous, current))
            findings.Add(new(ContractPath.Root(path), CompatibilityClassification.Unknown, "unclassified-model-facet",
                "This canonical facet changed but is not covered by the factual differ or compatibility policy.", null));
    }

    private static DataContract Prepare(CompatibilityInput input)
    {
        var contract = input.Contract!;
        if (input.Standard != ContractStandard.DataPackage) return contract;
        // DP required rejects null/missing values. ODCS required and nullable are independent.
        return contract with
        {
            Assets = contract.Assets.Select(asset => asset with
            {
                Schema = asset.Schema is { } schema ? schema with { Fields = schema.Fields.Select(Field).ToArray() } : null
            }).ToArray()
        };

        static DataField Field(DataField field) => field with
        {
            Nullable = !field.Required,
            Children = field.Children.Select(Field).ToArray(),
            Items = field.Items is { } items ? Field(items) : null
        };
    }
}
