using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.OpenDataContract.Mapping;
using CanonicalRule = Packata.Core.Contracts.DataQualityRule;

namespace Packata.Rules.Expressif.Testing;

public class ExpressifQualityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private static CanonicalRule Rule(string program = "positive", QualityProgramKind kind = QualityProgramKind.Predicate,
        QualityInputScope scope = QualityInputScope.Value, params QualityExpectation[] expectations)
        => new("custom") { SourceId = "test", Target = new("asset", ["value"]),
            Definition = new ExpressifQuality(program, kind, scope), Expectations = expectations };
    private static CompilationResult Compile(CanonicalRule rule) => new ExpressifQualityCompiler().Compile(rule, new());
    private static async Task<RuleResult> Execute(CanonicalRule rule, RuleExecutionContext context)
    {
        var compilation = Compile(rule);
        Assert.That(compilation.Diagnostics, Is.Empty);
        return (await RuleRunner.ExecuteAsync([compilation], context)).Results.Single();
    }

    [Test]
    public async Task NativeCustomPredicateMapsCompilesAndEvaluates()
    {
        var document = new OpenDataContract.DataContract { Id = "native", Schema = [new() { Name = "asset", Properties =
            [new() { Name = "value", Quality = [new() { Type = "custom", Engine = "expressif", Implementation =
                """{"version":"1","kind":"predicate","scope":"value","program":"positive"}""" }] }] }] };
        var rule = new OpenDataContractMapper().Map(document).Value!.Assets[0].Schema!.Fields[0].QualityRules[0];
        var result = await Execute(rule, new(Now, Value: QualityValue.From(2)));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Descriptor.Category, Is.EqualTo(RuleCategory.Quality));
        Assert.That(result.Descriptor.ImplementationVersion, Does.Contain("2.93.1"));
    }

    [Test]
    public async Task MeasuresWithTypedArgumentsAndExplicitExpectations()
    {
        var rule = Rule("add(@delta)", QualityProgramKind.Expression, expectations: [new(ExpectationOperator.Equal, QualityValue.From(4m))]);
        var result = await Execute(rule, new(Now, Value: QualityValue.From(1m), Bindings: new Dictionary<string, QualityValue> { ["delta"] = QualityValue.From(3m) }));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Expectations, Has.Count.EqualTo(1));
        Assert.That((await Execute(rule, new(Now, Value: QualityValue.From(1)))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [Test]
    public async Task FixedObservationTimeAndObservationScopeAreExplicit()
    {
        var rule = Rule("@observationTime", QualityProgramKind.Expression, QualityInputScope.Observation,
            new QualityExpectation(ExpectationOperator.Equal, QualityValue.From(Now)));
        Assert.That((await Execute(rule, new(Now, Observation: QualityValue.From(0)))).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That((await Execute(rule, new(Now))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [Test]
    public async Task RowAggregationCountsAllViolationsAndDisposesReaders()
    {
        var source = TestDataSource.Values(1, -1, 2);
        var result = await Execute(Rule(), new(Now, source));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Failed));
        Assert.That(result.Measurement.Population, Is.EqualTo(3));
        Assert.That(result.Measurement.Evidence.Single(e => e.Name == "violations").Value.ToObject(), Is.EqualTo(1));
        Assert.That(source.Readers.All(reader => reader.IsClosed), Is.True);
        var empty = await Execute(Rule(), new(Now, TestDataSource.Values()));
        Assert.That(empty.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [Test]
    public async Task RowExpressionsEvaluateExpectationsBeforeAggregation()
    {
        var rule = Rule("add(1)", QualityProgramKind.Expression, expectations: [new(ExpectationOperator.Greater, QualityValue.From(1))]);
        Assert.That((await Execute(rule, new(Now, TestDataSource.Values(1, 2)))).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That((await Execute(rule, new(Now, TestDataSource.Values(1, 0)))).Outcome, Is.EqualTo(RuleOutcome.Failed));
        var predicate = Rule() with { Expectations = [new(ExpectationOperator.Equal, QualityValue.From(false))] };
        Assert.That((await Execute(predicate, new(Now, TestDataSource.Values(-1, -2)))).Outcome, Is.EqualTo(RuleOutcome.Passed));
    }

    [Test]
    public async Task DatasetProgramsStreamAndReturnScalarMeasurements()
    {
        var rule = Rule("count", QualityProgramKind.Expression, QualityInputScope.Dataset,
            new QualityExpectation(ExpectationOperator.Equal, QualityValue.From(3)));
        var result = await Execute(rule, new(Now, TestDataSource.Values(1, 2, 3)));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Value!.ToObject(), Is.EqualTo(3));
        Assert.That(result.Measurement.Population, Is.EqualTo(3));
    }

    [Test]
    public async Task MissingFieldsAndNullAreDistinct()
    {
        var rule = Rule(".missing | positive", QualityProgramKind.Predicate, QualityInputScope.Row);
        Assert.That((await Execute(rule, new(Now, TestDataSource.Values(1)))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That((await Execute(Rule("null"), new(Now, Value: QualityValue.From(null)))).Outcome, Is.EqualTo(RuleOutcome.Passed));
    }

    [TestCase("missing-operation")]
    [TestCase("add(")]
    [TestCase("file-to-size")]
    public void InvalidOrUnsupportedProgramsHaveCompilationFindings(string program)
    {
        var compilation = Compile(Rule(program));
        Assert.That(compilation.Plans, Is.Empty);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void IncompatiblePredicateExpectationsAreInvalid()
    {
        var rule = Rule() with { Expectations = [new(ExpectationOperator.Equal, QualityValue.From(1))] };
        Assert.That(Compile(rule).Diagnostics.Single().Code, Is.EqualTo("PREDICATE_EXPECTATION"));
    }

    [Test]
    public async Task NoExpectationIncompleteInputAndBudgetsNeverPass()
    {
        var expression = Rule("neutral", QualityProgramKind.Expression);
        Assert.That((await Execute(expression, new(Now, Value: QualityValue.From(1)))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        var source = TestDataSource.Values(1, 2, 3);
        var result = await Execute(Rule(), new(Now, source, Limits: new(MaxRows: 2)));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(source.Readers.All(reader => reader.IsClosed), Is.True);
        var incomplete = new TestDataSource(new System.Data.DataTable(), complete: false);
        Assert.That((await Execute(Rule(), new(Now, incomplete))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [Test]
    public async Task OneShotInputIsNotReusedAndPlansAreIsolatedAcrossConcurrentRuns()
    {
        var a = Compile(Rule() with { SourceId = "a" });
        var b = Compile(Rule() with { SourceId = "b" });
        var table = new System.Data.DataTable(); table.Columns.Add("value", typeof(int)); table.Rows.Add(1);
        var report = await RuleRunner.ExecuteAsync([b, a], new(Now, new TestDataSource(table, replayable: false)));
        Assert.That(report.Results.Select(r => r.Outcome), Is.EqualTo(new[] { RuleOutcome.Passed, RuleOutcome.Skipped }));
        Assert.That(report.Passed, Is.False);
        var plan = a.Plans.Single();
        var results = await Task.WhenAll(Enumerable.Range(-5, 11).Select(async i => await plan.ExecuteAsync(new(Now, Value: QualityValue.From(i)))));
        Assert.That(results.Count(r => r.Outcome == RuleOutcome.Passed), Is.EqualTo(5));
    }
}
