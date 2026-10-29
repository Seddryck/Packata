using Packata.Core.Contracts;

namespace Packata.Core.Execution;

public sealed record QualityFilters
{
    public IReadOnlyList<string> Id { get; init; } = [];
    public IReadOnlyList<string> Asset { get; init; } = [];
    public IReadOnlyList<string> Field { get; init; } = [];
    public IReadOnlyList<string> Type { get; init; } = [];
    public IReadOnlyList<string> Dimension { get; init; } = [];

    public void Validate()
    {
        if (Field.Count > 0 && Asset.Count == 0) throw new ArgumentException("--field requires --asset.");
        if (new[] { Id, Asset, Field, Type, Dimension }.SelectMany(x => x).Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Quality filters require non-empty values.");
    }

    internal bool Matches(DataQualityRule rule) => Match(Id, rule.SourceId) && Match(Asset, rule.Target?.AssetId)
        && Match(Field, rule.Target is null ? null : string.Join('.', rule.Target.FieldPath))
        && Match(Type, rule.Kind) && Match(Dimension, rule.Metadata.Dimension);
    private static bool Match(IReadOnlyList<string> filter, string? value) => filter.Count == 0 || filter.Contains(value, StringComparer.Ordinal);
}

public sealed record QualityRuntime(string Name, string Version);
public sealed record QualityEngineInfo(string Id, string Version, IReadOnlyList<string> Types, QualityRuntime? Runtime = null);

/// <summary>An engine owns interpretation, compilation and individual verdicts; the runner owns orchestration.</summary>
public sealed record QualityEngine(QualityEngineInfo Info, IRuleCompiler<DataQualityRule> Compiler);
public sealed record QualityRunResult(DataQualityRule Rule, RuleOutcome Outcome, RuleResult? Execution, string? Message, string? EngineId);
public sealed record QualityRunReport(string Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, TimeSpan Duration,
    IReadOnlyList<QualityRunResult> Results, IReadOnlyList<QualityEngineInfo> Engines)
{
    public RuleOutcome Outcome => Results.Any(r => r.Outcome is RuleOutcome.Error or RuleOutcome.Inconclusive) ? RuleOutcome.Error
        : Results.Any(r => r.Outcome == RuleOutcome.Failed) ? RuleOutcome.Failed
        : Results.Any(r => r.Outcome == RuleOutcome.Warn) ? RuleOutcome.Warn
        : Results.Any(r => r.Outcome == RuleOutcome.Passed) ? RuleOutcome.Passed : RuleOutcome.Skipped;
    public int ExitCode => Outcome switch { RuleOutcome.Passed or RuleOutcome.Warn => 0, RuleOutcome.Failed => 1, _ => 2 };
}

public sealed class QualityRun
{
    private readonly QualityEngine[] _engines;
    public QualityRun(IEnumerable<QualityEngine> engines)
    {
        _engines = engines.ToArray();
        if (_engines.Select(e => e.Info.Id).Distinct(StringComparer.Ordinal).Count() != _engines.Length
            || _engines.SelectMany(e => e.Info.Types).GroupBy(t => t, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("Engine IDs must be unique and each rule type must map to at most one engine.");
    }

    public static IReadOnlyList<DataQualityRule> Select(DataContract contract, QualityFilters filters)
    {
        filters.Validate();
        var rules = contract.Assets.SelectMany(asset => asset.QualityRules.Concat(Fields(asset.Schema?.Fields ?? [])))
            .Where(filters.Matches).Select(r => r.Snapshot()).ToArray();
        if (rules.Length == 0) throw new ArgumentException("No quality rules match the supplied filters.");
        return Array.AsReadOnly(rules);
    }

    private static IEnumerable<DataQualityRule> Fields(IEnumerable<DataField> fields)
    {
        foreach (var field in fields)
        {
            foreach (var rule in field.QualityRules) yield return rule;
            foreach (var rule in Fields(field.Children)) yield return rule;
            if (field.Items is { } items)
                foreach (var rule in Fields([items])) yield return rule;
        }
    }

    public async Task<QualityRunReport> ExecuteAsync(DataContract contract, QualityFilters filters,
        Func<DataQualityRule, RuleExecutionContext> contextFactory, CancellationToken cancellationToken = default,
        TimeProvider? clock = null, DateTimeOffset? startedAt = null, long? startTimestamp = null)
    {
        clock ??= TimeProvider.System;
        var start = startedAt ?? clock.GetUtcNow();
        var timestamp = startTimestamp ?? clock.GetTimestamp();
        var selected = Select(contract, filters);
        var results = new List<QualityRunResult>();
        var participating = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExecuteRuleAsync(rule, contract, contextFactory, participating, clock, cancellationToken).ConfigureAwait(false));
        }
        return new(Guid.NewGuid().ToString(), start, clock.GetUtcNow(), clock.GetElapsedTime(timestamp), results.AsReadOnly(),
            ParticipatingEngines(participating, results));
    }

    private IReadOnlyList<QualityEngineInfo> ParticipatingEngines(HashSet<string> participating, IReadOnlyList<QualityRunResult> results)
        => Array.AsReadOnly(_engines.Where(e => participating.Contains(e.Info.Id)).Select(e => e.Info with
        {
            Types = e.Info.Types.Where(t => results.Any(r => r.EngineId == e.Info.Id && r.Rule.Kind == t)).ToArray()
        }).ToArray());

    private async Task<QualityRunResult> ExecuteRuleAsync(DataQualityRule rule, DataContract contract,
        Func<DataQualityRule, RuleExecutionContext> contextFactory, HashSet<string> participating,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (rule.Definition is TextQuality || rule.Kind == "text")
            return new(rule, RuleOutcome.Skipped, null, "Text quality rules are not executable.", null);
        var engine = _engines.SingleOrDefault(e => e.Info.Types.Contains(rule.Kind, StringComparer.Ordinal));
        if (engine is null)
            return new(rule, RuleOutcome.Error, null, "No engine is registered for this rule type.", null);
        participating.Add(engine.Info.Id);
        try
        {
            var context = contextFactory(rule);
            var compiled = engine.Compiler.Compile(rule, new(contract));
            if (compiled.Diagnostics.Count > 0 || compiled.Plans.Count != 1)
                return CompilationFailure(rule, engine.Info.Id, compiled);
            var (execution, failed) = await AttemptAsync(compiled.Plans[0], context, clock, cancellationToken).ConfigureAwait(false);
            var outcome = NormalizeOutcome(execution);
            var message = failed ? "Rule compilation, binding or execution failed." : Message(outcome);
            return new(rule, outcome, execution, message, engine.Info.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Compiler and binding exceptions may contain program text, raw records or secrets.
            return new(rule, RuleOutcome.Error, null, "Rule compilation, binding or execution failed.", engine.Info.Id);
        }
    }

    private static QualityRunResult CompilationFailure(DataQualityRule rule, string engineId, CompilationResult compiled)
    {
        var unsupported = compiled.Diagnostics.Count > 0 && compiled.Diagnostics.All(d => d.Kind == CompilationFindingKind.Unsupported);
        return new(rule, unsupported ? RuleOutcome.Skipped : RuleOutcome.Error, null,
            unsupported ? "The engine cannot evaluate this rule with its available capabilities."
                : "The rule configuration or required binding is invalid.", engineId);
    }

    private static async Task<(RuleResult Execution, bool Failed)> AttemptAsync(IExecutableRule plan, RuleExecutionContext context,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        var timestamp = clock.GetTimestamp();
        try { return (await plan.ExecuteAsync(context, cancellationToken).ConfigureAwait(false), false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Provider exception messages may contain secrets; preserve only execution timing.
            return (new(plan.Descriptor, RuleOutcome.Error, new(null, Complete: false), [], context.ObservationTime,
                started, clock.GetUtcNow(), clock.GetElapsedTime(timestamp)), true);
        }
    }

    private static RuleOutcome NormalizeOutcome(RuleResult execution)
    {
        // Incomplete measurements and timeouts cannot be reported as successful quality evaluations.
        if (execution.Outcome == RuleOutcome.Inconclusive) return RuleOutcome.Error;
        if (!execution.Measurement.Complete && execution.Outcome is RuleOutcome.Passed or RuleOutcome.Failed or RuleOutcome.Warn)
            return RuleOutcome.Error;
        return execution.Outcome;
    }

    private static string? Message(RuleOutcome outcome) => outcome switch
    {
        RuleOutcome.Failed => "The observed output did not satisfy the rule expectation.",
        RuleOutcome.Warn => "The observed output breached a warning threshold.",
        RuleOutcome.Error => "The rule could not be evaluated reliably. Check its configuration, input and runtime.",
        RuleOutcome.Skipped => "The selected rule was intentionally not executed.",
        _ => null
    };
}
