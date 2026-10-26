using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using NativeRule = Packata.OpenDataContract.DataQualityRule;

namespace Packata.OpenDataContract.Testing.Mapping;

public class QualityMappingTests
{
    private static MappingResult<Core.Contracts.DataContract> Map(NativeRule rule) => new OpenDataContractMapper().Map(new DataContract
    {
        Id = "test", Schema = [new() { Name = "asset", Quality = [rule], Properties =
            [new() { Name = "parent", Properties = [new() { Name = "child", Quality = [rule] }] }] }]
    });

    [TestCase("library", typeof(LibraryQuality))]
    [TestCase(null, typeof(LibraryQuality))]
    [TestCase("sql", typeof(SqlQuality))]
    [TestCase("text", typeof(TextQuality))]
    [TestCase("custom", typeof(CustomQuality))]
    public void MapsEveryNativeTypeAndNestedTarget(string? type, Type expected)
    {
        var result = Map(new() { Type = type, Metric = "rowCount", Query = "select 1", Engine = "unknown" });
        var rule = result.Value!.Assets[0].QualityRules[0];
        Assert.That(rule.Definition, Is.TypeOf(expected));
        Assert.That(rule.SourceId, Is.Null);
        Assert.That(rule.ExecutionId, Is.EqualTo("schema/asset/quality/0"));
        var nested = result.Value.Assets[0].Schema!.Fields[0].Children[0].QualityRules[0];
        Assert.That(nested.Target!.FieldPath, Is.EqualTo(new[] { "parent", "child" }));
    }

    [Test]
    public void PreservesAllExpectationsIncludingExplicitNullFalseAndZero()
    {
        var native = new NativeRule { Metric = "rowCount", MustBe = null, MustNotBe = false,
            MustBeGreaterThan = 0, MustBeGreaterOrEqualTo = 1, MustBeLessThan = 10,
            MustBeLessOrEqualTo = 9, MustBeBetween = [0, 10], MustNotBeBetween = [11, 20] };
        var rule = Map(native).Value!.Assets[0].QualityRules[0];
        Assert.That(rule.Expectations, Has.Count.EqualTo(8));
        Assert.That(rule.Expectations[0].Operand.ToObject(), Is.Null);
        Assert.That(rule.Expectations[1].Operand.ToObject(), Is.False);
        Assert.That(Map(new() { Metric = "rowCount" }).Value!.Assets[0].QualityRules[0].Expectations, Is.Empty);
        Assert.That(Map(new() { Metric = "rowCount", MustBeBetween = [] }).Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void TakesDetachedTypedArgumentsAndMetadataSnapshots()
    {
        var values = new List<object> { 1, 1.000000000000000000000000001m, false, "1" };
        var native = new NativeRule { Id = "source", Metric = "invalidValues", Arguments = new() { ["validValues"] = values },
            Name = "name", Description = "description", Dimension = "accuracy", BusinessImpact = "impact",
            Method = "method", Severity = "warning", Scheduler = "cron", Schedule = "* * * * *", Tags = ["tag"] };
        var rule = Map(native).Value!.Assets[0].QualityRules[0];
        values.Clear(); native.Tags.Clear(); native.Schedule = "changed";
        var args = ((LibraryQuality)rule.Definition!).Arguments;
        Assert.That((IReadOnlyList<QualityValue>)args["validValues"].Value!, Has.Count.EqualTo(4));
        Assert.That(rule.Metadata.Tags, Is.EqualTo(new[] { "tag" }));
        Assert.That(rule.Metadata.Schedule, Is.EqualTo("* * * * *"));
        Assert.That(rule.Extensions["odcs"]["declaration"], Is.Not.Null);
    }

    [Test]
    public void ParsesOnlyExplicitVersionedExpressifPayloads()
    {
        const string payload = """{"version":"1","kind":"predicate","scope":"value","program":"positive"}""";
        var rule = Map(new() { Type = "custom", Engine = "expressif", Implementation = payload }).Value!.Assets[0].QualityRules[0];
        Assert.That(rule.Definition, Is.EqualTo(new ExpressifQuality("positive", QualityProgramKind.Predicate, QualityInputScope.Value, OriginalPayload: payload)));
        var invalid = Map(new() { Type = "custom", Engine = "expressif", Implementation = "positive" });
        Assert.That(invalid.Diagnostics, Is.Not.Empty);
        Assert.That(invalid.Value!.Assets[0].QualityRules[0].Definition, Is.TypeOf<CustomQuality>());
    }

    [Test]
    public void RetainsDeprecatedMetricConflictAndInvalidOperandsWithDiagnostics()
    {
#pragma warning disable CS0618
        var result = Map(new() { Rule = "nullValues", Metric = "rowCount", MustBeBetween = [1] });
#pragma warning restore CS0618
        Assert.That(((LibraryQuality)result.Value!.Assets[0].QualityRules[0].Definition!).Metric, Is.EqualTo("rowCount"));
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Conflicting")), Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Invalid expectation")), Is.True);
    }

    [Test]
    public void QualityValuesRetainTemporalAndCalendarTypes()
    {
        Assert.That(QualityValue.From(new ContractDuration(1, TimeSpan.Zero)).Kind, Is.EqualTo(QualityValueKind.Duration));
        Assert.That(QualityValue.From(DateTimeOffset.UnixEpoch).Kind, Is.EqualTo(QualityValueKind.Timestamp));
        Assert.That(QualityValue.From(new DateOnly(2026, 1, 1)).Kind, Is.EqualTo(QualityValueKind.Date));
        Assert.That(() => QualityValue.From(DateTime.Now), Throws.ArgumentException);
    }
}
