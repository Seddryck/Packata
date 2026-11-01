using System.Data;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

public enum RuleCategory { Constraint,
    Quality,
    Sla }
public enum RuleOutcome { Passed,
    Failed,
    Inconclusive,
    Error,
    Skipped,
    Warn }
public enum CompilationFindingKind { Unsupported,
    Invalid,
    MissingBinding }

public sealed record RuleDescriptor(string ExecutionId, string? SourceId, string? SourcePath,
    RuleCategory Category, QualityTarget Target, string Backend, string ImplementationVersion,
    string? EndpointId = null, QualityMetadata? Metadata = null);
public sealed record InputRequirements(QualityInputScope Scope, bool RequiresCompleteInput = true);
public sealed record RuleDiagnostic(string Code, string Message);
public sealed record CompilationFinding(string ExecutionId, CompilationFindingKind Kind, string Code, string Message);
public sealed record CompilationResult(IReadOnlyList<IExecutableRule> Plans, IReadOnlyList<CompilationFinding> Diagnostics)
{
    public static CompilationResult Unsupported(DataQualityRule rule, string message) => new([], [new(rule.ExecutionId, CompilationFindingKind.Unsupported, "UNSUPPORTED", message)]);
}
public interface IRuleCompiler<in TDeclaration>
{
    CompilationResult Compile(TDeclaration declaration, RuleCompilationContext context);
}
public sealed record RuleCompilationContext(DataContract? Contract = null, string? EndpointId = null);

public interface IExecutableRule
{
    RuleDescriptor Descriptor { get; }
    InputRequirements Requirements { get; }
    ValueTask<RuleResult> ExecuteAsync(RuleExecutionContext context, CancellationToken cancellationToken = default);
}
public interface IMeasurementPlan
{
    ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken);
}
public interface IAssertionPlan
{
    IReadOnlyList<QualityExpectation> Expectations { get; }
    RuleOutcome Evaluate(QualityValue value);
}

/// <summary>Opening transfers reader ownership to the plan. Every open must observe the same explicit snapshot.</summary>
public interface IRuleDataSource
{
    string SnapshotId { get; }
    bool IsReplayable { get; }
    bool IsComplete { get; }
    ValueTask<IDataReader> OpenAsync(QualityTarget target, string? endpointId, CancellationToken cancellationToken);
}

public sealed record RuleLimits(long MaxRows = 100_000, int MaxEvidence = 10, int MaxStateEntries = 100_000, TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(30);
    public void Validate()
    {
        if (MaxRows < 0 || MaxEvidence < 0 || MaxStateEntries < 0 || EffectiveTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Run limits must be non-negative with a positive timeout.");
    }
}

public sealed record RuleExecutionContext(DateTimeOffset ObservationTime, IRuleDataSource? Data = null,
    QualityValue? Value = null, QualityValue? Observation = null,
    IReadOnlyDictionary<string, QualityValue>? Bindings = null, RuleLimits? Limits = null,
    TimeProvider? Clock = null)
{
    public RuleLimits Limits { get; init; } = Limits ?? new();
    public TimeProvider Clock { get; init; } = Clock ?? TimeProvider.System;
    public IReadOnlyDictionary<string, QualityValue> Bindings { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, QualityValue>((Bindings ?? new Dictionary<string, QualityValue>()).ToDictionary(p => p.Key, p => p.Value));
}

public sealed record RuleEvidence(string Name, QualityValue Value);
public sealed record RuleMeasurement(QualityValue? Value, string? Unit = null, long Population = 0,
    bool Complete = true, IReadOnlyList<RuleEvidence>? Evidence = null, IReadOnlyList<RuleDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<RuleEvidence> Evidence { get; init; } = Evidence ?? [];
    public IReadOnlyList<RuleDiagnostic> Diagnostics { get; init; } = Diagnostics ?? [];
}
public sealed record RuleResult(RuleDescriptor Descriptor, RuleOutcome Outcome, RuleMeasurement Measurement,
    IReadOnlyList<QualityExpectation> Expectations, DateTimeOffset ObservationTime,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, TimeSpan Duration);

public sealed record RuleEvaluationReport(IReadOnlyList<RuleResult> Results, IReadOnlyList<CompilationFinding> CompilationFindings)
{
    public bool IsComplete => CompilationFindings.Count == 0 && Results.All(r => r.Measurement.Complete && r.Outcome is RuleOutcome.Passed or RuleOutcome.Failed);
    public bool HasViolations => Results.Any(r => r.Outcome == RuleOutcome.Failed);
    public bool Passed => IsComplete && Results.Count > 0 && Results.All(r => r.Outcome == RuleOutcome.Passed);
}

/// <summary>A deliberate incomplete-input result; its public message must contain no raw data or secrets.</summary>
public sealed class IncompleteRuleInputException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
