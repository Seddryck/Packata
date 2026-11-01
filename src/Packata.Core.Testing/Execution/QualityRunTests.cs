using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Core.Testing.Execution;

public class QualityRunTests
{
    private static DataQualityRule Rule(string? id, string path, string type = "library", string? field = null, string dimension = "completeness")
        => new(type) { SourceId = id, SourcePath = path, Target = new("customers", field is null ? [] : [field]),
            Definition = type == "text" ? new TextQuality("Description") : new LibraryQuality("odcs", "3.2.0", "rowCount"),
            Metadata = new(Dimension: dimension) };
    private static DataContract Contract(params DataQualityRule[] rules) => new(new("contract"), new(),
        [new("customers", "customers", null, null, AssetKind.Table, null, QualityRules: rules)], [], new());
    private static QualityEngine Engine(IRuleCompiler<DataQualityRule> compiler) => new(new("test", "1", ["library"]), compiler);
    private static RuleExecutionContext Context(DataQualityRule rule) => new(DateTimeOffset.UtcNow);

    [TestCase(new[] { RuleOutcome.Skipped, RuleOutcome.Passed }, RuleOutcome.Passed)]
    [TestCase(new[] { RuleOutcome.Passed, RuleOutcome.Warn }, RuleOutcome.Warn)]
    [TestCase(new[] { RuleOutcome.Warn, RuleOutcome.Failed }, RuleOutcome.Failed)]
    [TestCase(new[] { RuleOutcome.Failed, RuleOutcome.Error }, RuleOutcome.Error)]
    public void AggregatePrecedenceDoesNotConfuseFailuresErrorsWarningsOrSkips(RuleOutcome[] outcomes, RuleOutcome expected)
    {
        var results = outcomes.Select((o, index) => new QualityRunResult(Rule(null, $"q/{index}"), o, null, null, null)).ToArray();
        var report = new QualityRunReport("run", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, results, []);
        Assert.That(report.Outcome, Is.EqualTo(expected));
    }

    [Test]
    public void SelectionCombinesAndOrPreservesOrderAndIncludesAnonymousNestedRules()
    {
        var z = Rule("z", "quality/0");
        var anonymous = Rule(null, "properties/email/quality/0", field: "email");
        var child = Rule("a", "properties/nested/quality/0", field: "email", dimension: "uniqueness");
        var contract = Contract(z) with { Assets = [Contract(z).Assets[0] with { Schema = new([
            new("email", "string", QualityRules: [anonymous], Children: [new("child", "string", QualityRules: [child])])]) }] };
        var selected = QualityRun.Select(contract, new() { Asset = ["customers"], Dimension = ["completeness", "uniqueness"] });
        Assert.That(selected.Select(r => r.SourceId), Is.EqualTo(new string?[] { "z", null, "a" }));
        Assert.That(QualityRun.Select(contract, new() { Asset = ["customers"], Field = ["email"], Dimension = ["uniqueness"] }).Single().SourceId, Is.EqualTo("a"));
        Assert.That(QualityRun.Select(contract, new() { Id = ["a", "z"], Type = ["library"] }).Select(r => r.SourceId), Is.EqualTo(new[] { "z", "a" }));
        Assert.Throws<ArgumentException>(() => QualityRun.Select(contract, new() { Id = ["missing"] }));
        Assert.Throws<ArgumentException>(() => QualityRun.Select(contract, new() { Field = ["email"] }));
    }

    [TestCase(RuleOutcome.Passed, 0)]
    [TestCase(RuleOutcome.Failed, 1)]
    [TestCase(RuleOutcome.Warn, 0)]
    [TestCase(RuleOutcome.Error, 2)]
    [TestCase(RuleOutcome.Inconclusive, 2)]
    [TestCase(RuleOutcome.Skipped, 2)]
    public async Task OutcomesAndExitCodesAreNormalized(RuleOutcome outcome, int exitCode)
    {
        var runner = new QualityRun([Engine(new Compiler(outcome))]);
        var report = await runner.ExecuteAsync(Contract(Rule("z", "q/0")), new(), Context);
        Assert.That(report.ExitCode, Is.EqualTo(exitCode));
        Assert.That(report.Outcome, Is.EqualTo(outcome == RuleOutcome.Inconclusive ? RuleOutcome.Error : outcome));
        Assert.That(report.Engines.Single().Id, Is.EqualTo("test"));
        Assert.That(report.Duration, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task EverySelectedRuleHasAResultEvenWhenCompilationDispatchOrExecutionFails()
    {
        var compiler = new Compiler(RuleOutcome.Passed);
        var contract = Contract(Rule("z", "q/0"), Rule("text", "q/1", "text"), Rule("missing", "q/2", "unknown"),
            Rule("invalid", "q/3"), Rule("unsupported", "q/4"), Rule("throws", "q/5"), Rule("a", "q/6"));
        var report = await new QualityRun([Engine(compiler)]).ExecuteAsync(contract, new(), Context);
        Assert.That(report.Results.Select(r => r.Rule.SourceId), Is.EqualTo(new[] { "z", "text", "missing", "invalid", "unsupported", "throws", "a" }));
        Assert.That(report.Results.Select(r => r.Outcome), Is.EqualTo(new[] { RuleOutcome.Passed, RuleOutcome.Skipped, RuleOutcome.Error,
            RuleOutcome.Error, RuleOutcome.Skipped, RuleOutcome.Error, RuleOutcome.Passed }));
        Assert.That(report.Results.Select(r => r.Message).OfType<string>(), Has.None.Contains("do-not-export"));
        Assert.That(report.ExitCode, Is.EqualTo(2));
        Assert.That(compiler.Seen, Is.EqualTo(new[] { "z", "invalid", "unsupported", "throws", "a" }));
        var text = await new QualityRun([Engine(compiler)]).ExecuteAsync(Contract(Rule(null, "q", "text")), new(), _ => throw new Exception());
        Assert.That(text.Engines, Is.Empty);
        Assert.That(text.Results.Single().Execution, Is.Null);
    }

    [Test]
    public async Task RegistrationRejectsAmbiguityAndCancellationPropagates()
    {
        var engine = Engine(new Compiler(RuleOutcome.Passed));
        Assert.Throws<ArgumentException>(() => new QualityRun([engine, engine]));
        Assert.Throws<ArgumentException>(() => new QualityRun([engine, engine with { Info = engine.Info with { Id = "other" } }]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new QualityRun([engine]).ExecuteAsync(Contract(Rule(null, "q")), new(), Context, cancelled.Token));
        var report = await new QualityRun([engine]).ExecuteAsync(Contract(Rule("z", "q/0"), Rule("a", "q/1")), new() { Id = ["a"] }, Context);
        Assert.That(report.Results.Single().Rule.SourceId, Is.EqualTo("a"));
    }

    private sealed class Compiler(RuleOutcome outcome) : IRuleCompiler<DataQualityRule>
    {
        public List<string?> Seen { get; } = [];
        public CompilationResult Compile(DataQualityRule rule, RuleCompilationContext context)
        {
            Seen.Add(rule.SourceId);
            return rule.SourceId switch
            {
                "invalid" => new([], [new(rule.ExecutionId, CompilationFindingKind.Invalid, "BAD", "do-not-export")]),
                "unsupported" => CompilationResult.Unsupported(rule, "do-not-export"),
                _ => new([new Plan(rule, outcome)], [])
            };
        }
    }
    private sealed class Plan(DataQualityRule rule, RuleOutcome outcome) : IExecutableRule
    {
        public RuleDescriptor Descriptor => QualityCompilation.Descriptor(rule, "test", "1");
        public InputRequirements Requirements => new(QualityInputScope.Observation);
        public ValueTask<RuleResult> ExecuteAsync(RuleExecutionContext context, CancellationToken cancellationToken)
        {
            if (rule.SourceId == "throws") throw new InvalidOperationException("do-not-export");
            return ValueTask.FromResult(new RuleResult(Descriptor, outcome, new(QualityValue.From(1)), [],
                context.ObservationTime, context.ObservationTime, context.ObservationTime, TimeSpan.FromMilliseconds(1)));
        }
    }
}
