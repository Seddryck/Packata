using System.Diagnostics;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>Reusable immutable composition. Measurement implementations must keep mutable state local to a run.</summary>
public sealed class MeasuredExecutableRule(RuleDescriptor descriptor, InputRequirements requirements,
    IMeasurementPlan measurement, IAssertionPlan assertion) : IExecutableRule
{
    public RuleDescriptor Descriptor { get; } = descriptor;
    public InputRequirements Requirements { get; } = requirements;

    public async ValueTask<RuleResult> ExecuteAsync(RuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Limits.Validate();
        var start = context.Clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(context.Limits.EffectiveTimeout, context.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        RuleMeasurement measured;
        RuleOutcome outcome;
        try
        {
            (measured, outcome) = await EvaluateAsync(context, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            measured = new(null, Complete: false, Diagnostics: [new("TIME_BUDGET", "The rule exceeded its time budget.")]);
            outcome = RuleOutcome.Inconclusive;
        }
        catch (IncompleteRuleInputException exception)
        {
            measured = new(null, Complete: false, Diagnostics: [new(exception.Code, "Required input is unavailable or a run limit was exhausted.")]);
            outcome = RuleOutcome.Inconclusive;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Provider messages, connection strings, program text and raw values may contain secrets.
            measured = new(null, Complete: false, Diagnostics: [new("EXECUTION_ERROR", $"Rule execution failed ({exception.GetType().Name}).")]);
            outcome = RuleOutcome.Error;
        }
        return new(Descriptor, outcome, measured, assertion.Expectations, context.ObservationTime,
            start, context.Clock.GetUtcNow(), watch.Elapsed);
    }
    private async ValueTask<(RuleMeasurement Measurement, RuleOutcome Outcome)> EvaluateAsync(RuleExecutionContext context, CancellationToken token)
    {
        var measured = await measurement.MeasureAsync(context, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var inputComplete = !Requirements.RequiresCompleteInput || context.Data?.IsComplete != false;
        measured = measured with
        {
            Complete = measured.Complete && inputComplete,
            Evidence = Array.AsReadOnly(measured.Evidence.Take(context.Limits.MaxEvidence).ToArray()),
            Diagnostics = Array.AsReadOnly(measured.Diagnostics.ToArray())
        };
        var outcome = measured.Complete && measured.Value is not null
            ? assertion.Evaluate(measured.Value) : RuleOutcome.Inconclusive;
        if (outcome == RuleOutcome.Inconclusive && measured.Diagnostics.Count == 0)
            measured = measured with { Diagnostics = [new("INSUFFICIENT_EVIDENCE", "A complete measurement and applicable expectation are required.")] };
        return (measured, outcome);
    }
}

public static class RuleRunner
{
    public static async ValueTask<RuleEvaluationReport> ExecuteAsync(IEnumerable<CompilationResult> compilations,
        RuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputs = compilations.ToArray();
        var findings = inputs.SelectMany(c => c.Diagnostics).ToList();
        var results = new List<RuleResult>();
        var dataConsumed = false;
        var plans = inputs.SelectMany(c => c.Plans).OrderBy(p => p.Descriptor.ExecutionId, StringComparer.Ordinal).ToArray();
        var duplicates = plans.GroupBy(p => p.Descriptor.ExecutionId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (duplicates.Contains(plan.Descriptor.ExecutionId))
            {
                findings.Add(new(plan.Descriptor.ExecutionId, CompilationFindingKind.Invalid, "DUPLICATE_ID", "Execution identities must be unique within a report."));
                continue;
            }
            var consumesData = (plan.Requirements.Scope is QualityInputScope.Row or QualityInputScope.Dataset) || (plan.Requirements.Scope == QualityInputScope.Value && context.Value is null);
            if (consumesData && dataConsumed && context.Data?.IsReplayable == false)
            {
                var now = context.Clock.GetUtcNow();
                results.Add(new(plan.Descriptor, RuleOutcome.Skipped,
                    new(null, Complete: false, Diagnostics: [new("ONE_SHOT_INPUT", "Multiple plans require a replayable snapshot or separate explicit inputs.")]),
                    [], context.ObservationTime, now, now, TimeSpan.Zero));
                continue;
            }
            results.Add(await plan.ExecuteAsync(context, cancellationToken).ConfigureAwait(false));
            dataConsumed |= consumesData;
        }
        return new(Array.AsReadOnly(results.ToArray()), Array.AsReadOnly(findings
            .OrderBy(f => f.ExecutionId, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ToArray()));
    }
}

public static class QualityCompilation
{
    public static IReadOnlyList<CompilationFinding> Validate(DataQualityRule rule)
    {
        var findings = ExpectationAssertionPlan.Validate(rule.Expectations)
            .Select(message => new CompilationFinding(rule.ExecutionId, CompilationFindingKind.Invalid, "INVALID_EXPECTATION", message)).ToList();
        if (rule.Target is null || string.IsNullOrEmpty(rule.Target.AssetId))
            findings.Add(new(rule.ExecutionId, CompilationFindingKind.MissingBinding, "MISSING_TARGET", "An explicit asset target is required."));
        if (rule.Diagnostics.Any(d => d.Message.StartsWith("Invalid", StringComparison.Ordinal)))
            findings.Add(new(rule.ExecutionId, CompilationFindingKind.Invalid, "INVALID_DECLARATION", "The native declaration has invalid operands or an invalid definition; correct mapping diagnostics first."));
        return findings;
    }

    public static RuleDescriptor Descriptor(DataQualityRule rule, string backend, string version, string? endpointId = null)
        => new(rule.ExecutionId, rule.SourceId, rule.SourcePath, RuleCategory.Quality,
            rule.Target ?? throw new ArgumentException("An explicit target is required.", nameof(rule)), backend, version, endpointId, rule.Metadata);
}

/// <summary>Retains text/custom declarations as findings until a concrete adapter is supplied.</summary>
public sealed class UnsupportedQualityCompiler : IRuleCompiler<DataQualityRule>
{
    public CompilationResult Compile(DataQualityRule declaration, RuleCompilationContext context)
        => CompilationResult.Unsupported(declaration, "This declaration has no executable measurement implementation.");
}
