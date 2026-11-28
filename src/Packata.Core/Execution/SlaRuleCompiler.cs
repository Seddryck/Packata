using System.Globalization;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>Compiles normalized SLA declarations against explicit deployment bindings.</summary>
public sealed class SlaRuleCompiler(SlaEvaluationBindings bindings) : IRuleCompiler<Sla>
{
    public CompilationResult Compile(Sla declaration, RuleCompilationContext context)
    {
        var plans = new List<IExecutableRule>();
        var findings = new List<CompilationFinding>();
        var declared = new HashSet<SlaCommitmentReference>();
        var byCommitment = bindings.Bindings.ToLookup(binding => binding.Commitment);
        foreach (var (reference, commitment) in Commitments(declaration))
        {
            declared.Add(reference);
            var identity = Identity(reference);
            var matches = byCommitment[reference].ToArray();
            var finding = Validate(reference.Kind, commitment, matches, context);
            if (finding is { } problem)
            {
                findings.Add(new(identity, problem.Kind, problem.Code, problem.Message));
                continue;
            }

            var binding = matches[0];
            var descriptor = new RuleDescriptor(identity, bindings.SlaId, Path(reference), RuleCategory.Sla,
                binding.Target, "packata.sla", "1", binding.EndpointId) { SlaBinding = binding };
            plans.Add(new SlaRulePlan(descriptor, binding, commitment.Duration, commitment.Percentage, commitment.Window));
        }

        foreach (var extra in byCommitment.Where(group => !declared.Contains(group.Key)))
        {
            findings.Add(new(Identity(extra.Key), CompilationFindingKind.Invalid, "UNKNOWN_SLA_COMMITMENT",
                "The binding refers to an absent SLA commitment."));
        }

        return new(plans.AsReadOnly(), findings.AsReadOnly());
    }

    private string Identity(SlaCommitmentReference reference) => $"sla:{bindings.SlaId}:{Path(reference)}";

    private static string Path(SlaCommitmentReference reference)
    {
        var category = reference.Kind switch
        {
            SlaCommitmentKind.Freshness => "freshness",
            SlaCommitmentKind.Latency => "latency",
            SlaCommitmentKind.Frequency => "frequency",
            SlaCommitmentKind.Retention => "retention",
            SlaCommitmentKind.AvailabilityTarget => "availability/target",
            SlaCommitmentKind.AvailabilityWindow => "availability/windows",
            _ => reference.Kind.ToString()
        };
        return $"/sla/{category}/{reference.Index.ToString(CultureInfo.InvariantCulture)}";
    }

    private static BindingFinding? Validate(SlaCommitmentKind kind, Commitment commitment,
        SlaEvaluationBinding[] matches, RuleCompilationContext context)
    {
        var declarationFinding = ValidateCommitment(commitment);
        if (declarationFinding is not null)
            return declarationFinding;
        if (matches.Length == 0)
            return new(CompilationFindingKind.MissingBinding, "MISSING_SLA_BINDING", "The commitment needs an explicit observation binding.");
        if (matches.Length > 1)
            return new(CompilationFindingKind.Invalid, "AMBIGUOUS_SLA_BINDING", "Multiple bindings select the same commitment.");

        return ValidateBinding(kind, matches[0]) ?? ValidateContext(commitment, matches[0], context);
    }

    private static BindingFinding? ValidateCommitment(Commitment commitment)
    {
        if (commitment.Window is { End: null } || (commitment.Window is { } window && window.End == window.Start))
            return new(CompilationFindingKind.Unsupported, "UNSUPPORTED_SLA_WINDOW", "Daily availability needs distinct start and end times.");
        if (commitment.Elements.Count > 1)
            return new(CompilationFindingKind.Unsupported, "UNSUPPORTED_SLA_SCOPE", "This policy requires a commitment with at most one declared element.");
        if ((commitment.Duration is { } duration && !NonNegative(duration)) || commitment.Percentage is < 0 or > 100
            || (commitment.Window?.UtcOffset is { } offset && !ValidOffset(offset)))
            return new(CompilationFindingKind.Invalid, "INVALID_SLA_COMMITMENT", "The normalized SLA operand is outside its supported range.");
        return null;
    }

    private static bool ValidOffset(TimeSpan offset)
        => offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14) && offset.Ticks % TimeSpan.TicksPerMinute == 0;

    private static BindingFinding? ValidateBinding(SlaCommitmentKind kind, SlaEvaluationBinding binding)
    {
        if (binding.Policy == SlaMeasurementPolicy.Unspecified)
            return new(CompilationFindingKind.MissingBinding, "MISSING_SLA_POLICY", "The binding must declare a measurement policy.");
        if (binding.Policy != PolicyFor(kind))
            return new(CompilationFindingKind.Unsupported, "UNSUPPORTED_SLA_POLICY", "The measurement policy does not support this commitment kind.");
        if (!HasRequiredFields(kind, binding))
        {
            return new(CompilationFindingKind.MissingBinding, "INCOMPLETE_SLA_BINDING",
                "An explicit target, endpoint, source, window and all policy fields are required.");
        }
        if (!ValidWindow(kind, binding.Lookback) || !DistinctFields(kind, binding))
            return new(CompilationFindingKind.Invalid, "INVALID_SLA_BINDING", "The window and policy fields must be distinct and valid.");
        return null;
    }

    private static bool HasRequiredFields(SlaCommitmentKind kind, SlaEvaluationBinding binding)
        => !string.IsNullOrWhiteSpace(binding.Target?.AssetId)
            && !string.IsNullOrWhiteSpace(binding.EndpointId) && !string.IsNullOrWhiteSpace(binding.SourceId)
            && !string.IsNullOrWhiteSpace(binding.TimestampField)
            && (!NeedsTwoTimestamps(kind) || !string.IsNullOrWhiteSpace(binding.EndTimestampField))
            && (!IsAvailability(kind) || !string.IsNullOrWhiteSpace(binding.AvailabilityField))
            && (kind == SlaCommitmentKind.AvailabilityWindow || binding.Lookback is not null);

    private static bool ValidWindow(SlaCommitmentKind kind, ContractDuration? lookback)
        => kind == SlaCommitmentKind.AvailabilityWindow ? lookback is null
            : lookback is { } duration && duration != default && NonNegative(duration);

    private static bool DistinctFields(SlaCommitmentKind kind, SlaEvaluationBinding binding)
        => (!NeedsTwoTimestamps(kind) || binding.TimestampField != binding.EndTimestampField)
            && (!IsAvailability(kind) || (binding.TimestampField != binding.AvailabilityField && binding.EndTimestampField != binding.AvailabilityField));

    private static BindingFinding? ValidateContext(Commitment commitment, SlaEvaluationBinding binding, RuleCompilationContext context)
    {
        if (binding.CalendarTimeZone is null && (commitment.Duration?.CalendarMonths > 0 || binding.Lookback?.CalendarMonths > 0
            || commitment.Window is { UtcOffset: null }))
        {
            return new(CompilationFindingKind.MissingBinding, "MISSING_SLA_TIME_ZONE",
                "Calendar months and offsetless daily windows require an explicit calendar time zone.");
        }
        if (context.EndpointId is not null && context.EndpointId != binding.EndpointId)
            return TargetMismatch();

        DataAsset? asset = null;
        if (context.Contract is { } contract)
        {
            asset = FindBoundAsset(binding, contract);
            if (asset is null)
                return TargetMismatch();
        }

        if (commitment.Elements.Count == 0 || MatchesElement(binding.Target, commitment.Elements[0], asset))
            return null;
        return TargetMismatch();
    }

    private static DataAsset? FindBoundAsset(SlaEvaluationBinding binding, DataContract contract)
    {
        var assets = contract.Assets.Where(candidate => candidate.Id == binding.Target.AssetId).Take(2).ToArray();
        if (assets.Length != 1 || contract.Endpoints.Count(endpoint => endpoint.Id == binding.EndpointId) != 1)
            return null;
        var asset = assets[0];
        return asset.EndpointBindings.Count == 0 || asset.EndpointBindings.Any(endpoint => endpoint.EndpointId == binding.EndpointId)
            ? asset : null;
    }

    private static bool MatchesElement(QualityTarget target, ElementReference element, DataAsset? asset)
        => (element.Dataset == target.AssetId || element.Dataset == asset?.Name)
            && (element.Field is null ? target.FieldPath.Count == 0 : element.Field == string.Join('.', target.FieldPath));

    private static BindingFinding TargetMismatch()
        => new(CompilationFindingKind.Invalid, "SLA_TARGET_MISMATCH", "The binding target or endpoint does not match the selected contract scope.");

    private sealed record BindingFinding(CompilationFindingKind Kind, string Code, string Message);

    private static bool NonNegative(ContractDuration duration) => duration.CalendarMonths >= 0 && duration.Elapsed >= TimeSpan.Zero;

    private static bool NeedsTwoTimestamps(SlaCommitmentKind kind)
        => kind is SlaCommitmentKind.Latency or SlaCommitmentKind.Retention or SlaCommitmentKind.AvailabilityTarget or SlaCommitmentKind.AvailabilityWindow;

    private static bool IsAvailability(SlaCommitmentKind kind)
        => kind is SlaCommitmentKind.AvailabilityTarget or SlaCommitmentKind.AvailabilityWindow;

    private static SlaMeasurementPolicy PolicyFor(SlaCommitmentKind kind) => kind switch
    {
        SlaCommitmentKind.Freshness => SlaMeasurementPolicy.AgeOfLatestTimestamp,
        SlaCommitmentKind.Latency => SlaMeasurementPolicy.MaximumPairedDeliveryLatency,
        SlaCommitmentKind.Frequency => SlaMeasurementPolicy.MaximumTimeSinceUpdate,
        SlaCommitmentKind.Retention => SlaMeasurementPolicy.ContinuousRetainedCoverage,
        SlaCommitmentKind.AvailabilityTarget => SlaMeasurementPolicy.TimeWeightedAvailability,
        SlaCommitmentKind.AvailabilityWindow => SlaMeasurementPolicy.ContinuousAvailabilityInDailyWindow,
        _ => SlaMeasurementPolicy.Unspecified
    };

    private static IEnumerable<(SlaCommitmentReference Reference, Commitment Commitment)> Commitments(Sla sla)
    {
        foreach (var item in Durations(sla.Freshness, SlaCommitmentKind.Freshness)
            .Concat(Durations(sla.Latency, SlaCommitmentKind.Latency))
            .Concat(Durations(sla.Frequency, SlaCommitmentKind.Frequency))
            .Concat(Durations(sla.Retention, SlaCommitmentKind.Retention)))
            yield return item;
        if (sla.Availability is not { } availability)
            yield break;
        for (var index = 0; index < availability.Target.Count; index++)
        {
            var target = availability.Target[index];
            yield return (new(SlaCommitmentKind.AvailabilityTarget, index), new(target.Elements, Percentage: target.Percentage));
        }
        for (var index = 0; index < availability.Windows.Count; index++)
        {
            var window = availability.Windows[index];
            yield return (new(SlaCommitmentKind.AvailabilityWindow, index), new(window.Elements, Percentage: 100, Window: window));
        }
    }

    private static IEnumerable<(SlaCommitmentReference Reference, Commitment Commitment)> Durations(
        IReadOnlyList<DurationCommitment> items, SlaCommitmentKind kind)
        => items.Select((item, index) => (new SlaCommitmentReference(kind, index), new Commitment(item.Elements, item.Duration)));

    private sealed record Commitment(IReadOnlyList<ElementReference> Elements,
        ContractDuration? Duration = null, decimal? Percentage = null, AvailabilityWindow? Window = null);
}
