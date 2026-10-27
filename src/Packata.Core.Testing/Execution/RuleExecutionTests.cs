using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Core.Testing.Execution;

public class RuleExecutionTests
{
    private static QualityValue V(object? value) => QualityValue.From(value);
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private sealed class Measurement(Func<RuleExecutionContext, CancellationToken, ValueTask<RuleMeasurement>> measure) : IMeasurementPlan
    {
        public ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken) => measure(context, cancellationToken);
    }
    private static IExecutableRule Rule(string id, IMeasurementPlan measure, RuleCategory category = RuleCategory.Quality,
        params QualityExpectation[] expectations) => new MeasuredExecutableRule(new(id, null, id, category,
            new("asset"), "example", "1"), new(QualityInputScope.Observation), measure, new ExpectationAssertionPlan(expectations));
    private static CompilationResult Compiled(params IExecutableRule[] plans) => new(plans, []);
    private static Measurement Constant(object? value, bool complete = true) => new Measurement((_, _) => ValueTask.FromResult(new RuleMeasurement(V(value), Complete: complete)));

    [TestCase(ExpectationOperator.Equal, 5, 5, true)]
    [TestCase(ExpectationOperator.NotEqual, 4, 5, true)]
    [TestCase(ExpectationOperator.Greater, 6, 5, true)]
    [TestCase(ExpectationOperator.GreaterOrEqual, 5, 5, true)]
    [TestCase(ExpectationOperator.Less, 4, 5, true)]
    [TestCase(ExpectationOperator.LessOrEqual, 5, 5, true)]
    [TestCase(ExpectationOperator.Between, 5, 0, true)]
    [TestCase(ExpectationOperator.Between, 0, 0, false)]
    [TestCase(ExpectationOperator.NotBetween, 0, 0, true)]
    [TestCase(ExpectationOperator.NotBetween, 5, 0, false)]
    public void EvaluatesTypedOperators(ExpectationOperator op, int actual, int operand, bool expected)
    {
        var range = op is ExpectationOperator.Between or ExpectationOperator.NotBetween;
        Assert.That(ExpectationAssertionPlan.Matches(V(actual), new(op, V(operand), range ? V(10) : null)), Is.EqualTo(expected));
    }

    [Test]
    public void ComparisonsPreservePrecisionTypesNullsAndCalendarDurations()
    {
        Assert.That(TypedComparison.Compare(V(9007199254740993L), V(9007199254740992d)), Is.Positive);
        Assert.That(TypedComparison.Compare(V(1.000000000000000000000000001m), V(1m)), Is.Positive);
        Assert.That(TypedComparison.Equal(V(1), V(1m)), Is.True);
        Assert.That(TypedComparison.Equal(V(false), V(0)), Is.False);
        Assert.That(TypedComparison.Equal(V(null), V(null)), Is.True);
        Assert.That(() => TypedComparison.Compare(V("10"), V("2")), Throws.ArgumentException);
        Assert.That(TypedComparison.Compare(V(ObservedAt), V(ObservedAt.AddMinutes(1))), Is.Negative);
        Assert.That(() => TypedComparison.Compare(V(new ContractDuration(1, TimeSpan.Zero)), V(TimeSpan.FromDays(30))), Throws.ArgumentException);
    }

    [Test]
    public void ValidatesMalformedAndConflictingConjunctions()
    {
        Assert.That(ExpectationAssertionPlan.Validate([new(ExpectationOperator.Between, V(1))]), Is.Not.Empty);
        Assert.That(ExpectationAssertionPlan.Validate([new(ExpectationOperator.Less, V(true))]), Is.Not.Empty);
        Assert.That(ExpectationAssertionPlan.Validate([new(ExpectationOperator.Greater, V(2)), new(ExpectationOperator.LessOrEqual, V(2))]), Is.Not.Empty);
        Assert.That(ExpectationAssertionPlan.Validate([new(ExpectationOperator.Equal, V(2)), new(ExpectationOperator.NotEqual, V(2))]), Is.Not.Empty);
        Assert.That(ExpectationAssertionPlan.Validate([new(ExpectationOperator.GreaterOrEqual, V(2)), new(ExpectationOperator.LessOrEqual, V(2))]), Is.Empty);
        var plan = new ExpectationAssertionPlan([new(ExpectationOperator.Greater, V(2)), new(ExpectationOperator.Less, V(4))]);
        Assert.That(plan.Evaluate(V(3)), Is.EqualTo(RuleOutcome.Passed));
        Assert.That(plan.Evaluate(V(4)), Is.EqualTo(RuleOutcome.Failed));
    }

    [Test]
    public async Task AggregationRetainsCategoriesAndStableOrderingWithoutFalsePass()
    {
        var plans = Compiled(Rule("z", Constant(1), RuleCategory.Sla, new QualityExpectation(ExpectationOperator.Equal, V(1))),
            Rule("a", Constant(2), RuleCategory.Constraint, new QualityExpectation(ExpectationOperator.Equal, V(1))));
        var report = await RuleRunner.ExecuteAsync([plans], new(ObservedAt));
        Assert.That(report.IsComplete, Is.True);
        Assert.That(report.HasViolations, Is.True);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Results.Select(r => r.Descriptor.ExecutionId), Is.EqualTo(new[] { "a", "z" }));
        Assert.That(report.Results.Select(r => r.Descriptor.Category), Is.EqualTo(new[] { RuleCategory.Constraint, RuleCategory.Sla }));
        var unsupported = new UnsupportedQualityCompiler().Compile(new("text"), new());
        var partial = await RuleRunner.ExecuteAsync([plans, unsupported], new(ObservedAt));
        Assert.That(partial.IsComplete, Is.False);
        Assert.That(partial.CompilationFindings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NoExpectationIncompleteInputOrMissingObservationNeverPasses()
    {
        var report = await RuleRunner.ExecuteAsync([Compiled(Rule("number", Constant(1)), Rule("partial", Constant(1, false),
            expectations: [new(ExpectationOperator.Equal, V(1))]), Rule("missing", new Measurement((_, _) => ValueTask.FromResult(new RuleMeasurement(null)))))], new(ObservedAt));
        Assert.That(report.Results.All(r => r.Outcome == RuleOutcome.Inconclusive), Is.True);
        Assert.That(report.IsComplete, Is.False);
        Assert.That(report.Passed, Is.False);
        Assert.That((await RuleRunner.ExecuteAsync([], new(ObservedAt))).Passed, Is.False);
    }

    [Test]
    public async Task ErrorsAreSanitizedAndEvidenceIsBounded()
    {
        var bad = Rule("error", new Measurement((_, _) => throw new InvalidOperationException("password=secret")));
        var evidence = Rule("evidence", new Measurement((_, _) => ValueTask.FromResult(new RuleMeasurement(V(true),
            Evidence: Enumerable.Range(0, 100).Select(i => new RuleEvidence("count", V(i))).ToArray()))));
        var unexpectedCancellation = Rule("unexpected-cancel", new Measurement((_, _) => throw new OperationCanceledException()));
        var report = await RuleRunner.ExecuteAsync([Compiled(bad, evidence, unexpectedCancellation)], new(ObservedAt, Limits: new(MaxEvidence: 2)));
        Assert.That(report.Results[0].Outcome, Is.EqualTo(RuleOutcome.Error));
        Assert.That(report.Results[0].Measurement.Diagnostics[0].Message, Does.Not.Contain("secret"));
        Assert.That(report.Results[1].Measurement.Evidence, Has.Count.EqualTo(2));
        Assert.That(report.Results[2].Outcome, Is.EqualTo(RuleOutcome.Error));
    }

    [Test]
    public async Task TimeBudgetAndCallerCancellationHaveExplicitSemantics()
    {
        var slow = Rule("slow", new Measurement(async (_, token) => { await Task.Delay(1000, token); return new(V(true)); }));
        var report = await RuleRunner.ExecuteAsync([Compiled(slow)], new(ObservedAt, Limits: new(Timeout: TimeSpan.FromMilliseconds(10))));
        Assert.That(report.Results[0].Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await RuleRunner.ExecuteAsync([Compiled(slow)], new(ObservedAt), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task SharedPlanHasNoMutableCrossRunState()
    {
        var plan = Rule("value", new Measurement((context, _) => ValueTask.FromResult(new RuleMeasurement(context.Value))),
            expectations: [new(ExpectationOperator.Equal, V(1))]);
        var runs = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i => await plan.ExecuteAsync(new(ObservedAt, Value: V(i)))));
        Assert.That(runs.Count(r => r.Outcome == RuleOutcome.Passed), Is.EqualTo(1));
        Assert.That(runs.All(r => r.ObservationTime == ObservedAt), Is.True);
    }
}
