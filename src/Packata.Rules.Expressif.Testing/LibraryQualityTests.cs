using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif.Testing;

public class LibraryQualityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private static DataQualityRule Rule(string metric, decimal expected, string unit = "rows",
        bool field = true, Dictionary<string, QualityValue>? arguments = null)
        => new("library") { SourceId = "metric", Target = new("asset", field ? ["value"] : []),
            Definition = new LibraryQuality("odcs", "3.2.0", metric, arguments, unit),
            Expectations = [new(ExpectationOperator.Equal, QualityValue.From(expected))] };
    private static async Task<RuleResult> Execute(DataQualityRule rule, IRuleDataSource source, RuleLimits? limits = null)
    {
        var compilation = new LibraryQualityCompiler().Compile(rule, new());
        Assert.That(compilation.Diagnostics, Is.Empty);
        return (await RuleRunner.ExecuteAsync([compilation], new(Now, source, Limits: limits))).Results.Single();
    }

    [Test]
    public async Task AllFiveMetricsHaveExecutableCatalogPrograms()
    {
        var fixtures = new[]
        {
            (Rule("rowCount", 4, field: false), TestDataSource.Values(1, 2, 3, 4)),
            (Rule("nullValues", 1), TestDataSource.Values(null, 0, "", "N/A")),
            (Rule("missingValues", 2, arguments: new() { ["missingValues"] = QualityValue.From(new object?[] { null, "N/A" }) }), TestDataSource.Values(null, "N/A", "", 0)),
            (Rule("invalidValues", 2, arguments: new() { ["validValues"] = QualityValue.From(new object?[] { "A", "B" }) }), TestDataSource.Values("A", "B", "C", null)),
            (Rule("duplicateValues", 2), TestDataSource.Values("A", "A", "A", "B"))
        };
        foreach (var (rule, source) in fixtures)
        {
            var result = await Execute(rule, source);
            Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed), ((LibraryQuality)rule.Definition!).Metric);
            Assert.That(result.Measurement.Population, Is.EqualTo(4));
            Assert.That(result.Descriptor.ImplementationVersion, Does.Contain("packata-odcs/1"));
            Assert.That(source.Readers.All(r => r.IsClosed), Is.True);
        }
    }

    [Test]
    public async Task TypedMembershipDoesNotEquateNullEmptyMarkersOrNumericStrings()
    {
        var rule = Rule("missingValues", 2, arguments: new() { ["missingValues"] = QualityValue.From(new object?[] { null, 1 }) });
        var result = await Execute(rule, TestDataSource.Values(null, "", "N/A", 1, 1m, "1", false));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Value!.ToObject(), Is.EqualTo(2m));
    }

    [Test]
    public async Task PatternsAndValidValuesAreConjunctiveAndArgumentsAreNeverInterpolated()
    {
        var args = new Dictionary<string, QualityValue> { ["validValues"] = QualityValue.From(new object?[] { "AX", "BX", null, "quote') | invalid" }), ["pattern"] = QualityValue.From("^A") };
        var rule = Rule("invalidValues", 3, arguments: args);
        Assert.That((await Execute(rule, TestDataSource.Values("AX", "BX", null, "AZ"))).Outcome, Is.EqualTo(RuleOutcome.Passed));
        var patternOnly = Rule("invalidValues", 2, arguments: new() { ["pattern"] = QualityValue.From("^A") });
        Assert.That((await Execute(patternOnly, TestDataSource.Values("AX", "BX", null))).Outcome, Is.EqualTo(RuleOutcome.Passed));
    }

    [Test]
    public async Task PercentagesRetainNumeratorAndDenominatorAndBoundaryThresholds()
    {
        var rule = Rule("nullValues", 25, "percent");
        var result = await Execute(rule, TestDataSource.Values(null, 1, 2, 3));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Unit, Is.EqualTo("percent"));
        Assert.That(result.Measurement.Evidence.Select(e => e.Value.ToObject()), Is.EqualTo(new object[] { 1L, 4L }));
        var threshold = rule with { Expectations = [new(ExpectationOperator.Less, QualityValue.From(25))] };
        Assert.That((await Execute(threshold, TestDataSource.Values(null, 1, 2, 3))).Outcome, Is.EqualTo(RuleOutcome.Failed));
    }

    [Test]
    public async Task EmptyInputsHaveExplicitSemantics()
    {
        Assert.That((await Execute(Rule("rowCount", 0, field: false), TestDataSource.Values())).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That((await Execute(Rule("nullValues", 0), TestDataSource.Values())).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That((await Execute(Rule("nullValues", 0, "percent"), TestDataSource.Values())).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [Test]
    public async Task CompositeDuplicatesCountExcessOccurrencesWithTypedKeysAndNulls()
    {
        var table = new DataTable(); table.Columns.Add("tenant", typeof(object)); table.Columns.Add("id", typeof(object));
        table.Rows.Add("a", 1); table.Rows.Add("a", 1); table.Rows.Add("a", "1");
        table.Rows.Add("b", DBNull.Value); table.Rows.Add("b", DBNull.Value); table.Rows.Add("b", DBNull.Value);
        var rule = Rule("duplicateValues", 3, field: false, arguments: new() { ["properties"] = QualityValue.From(new[] { "tenant", "id" }) });
        Assert.That((await Execute(rule, new TestDataSource(table))).Outcome, Is.EqualTo(RuleOutcome.Passed));
        var percent = rule with { Definition = new LibraryQuality("odcs", "3.2.0", "duplicateValues", ((LibraryQuality)rule.Definition!).Arguments, "percent"),
            Expectations = [new(ExpectationOperator.Equal, QualityValue.From(50m))] };
        Assert.That((await Execute(percent, new TestDataSource(table))).Outcome, Is.EqualTo(RuleOutcome.Passed));
    }

    [Test]
    public async Task ResourceLimitsAndIncompleteScansNeverPass()
    {
        var source = TestDataSource.Values(1, 2, 3);
        Assert.That((await Execute(Rule("duplicateValues", 0), source, new(MaxStateEntries: 2))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That((await Execute(Rule("rowCount", 2, field: false), source, new(MaxRows: 2))).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(source.Readers.All(r => r.IsClosed), Is.True);
    }

    [TestCase("unknown", "rows", false)]
    [TestCase("rowCount", "percent", false)]
    [TestCase("rowCount", "rows", true)]
    [TestCase("nullValues", "rows", false)]
    public void UnknownMetricsUnsupportedUnitsAndScopesAreDiagnosed(string metric, string unit, bool field)
    {
        var compilation = new LibraryQualityCompiler().Compile(Rule(metric, 0, unit, field), new());
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
        Assert.That(compilation.Plans, Is.Empty);
    }

    [Test]
    public void InvalidArgumentsMissingVersionsAndImplementationsAreDiagnosed()
    {
        var rules = new[]
        {
            Rule("missingValues", 0),
            Rule("invalidValues", 0),
            Rule("invalidValues", 0, arguments: new() { ["pattern"] = QualityValue.From("[") }),
            Rule("invalidValues", 0, arguments: new() { ["validValues"] = QualityValue.From("A") }),
            Rule("nullValues", 0, arguments: new() { ["unexpected"] = QualityValue.From(false) }),
            Rule("duplicateValues", 0, field: false),
            Rule("rowCount", 0, field: false) with { Definition = new LibraryQuality("odcs", "next", "rowCount") }
        };
        foreach (var rule in rules) Assert.That(new LibraryQualityCompiler().Compile(rule, new()).Diagnostics, Is.Not.Empty);
    }
}
