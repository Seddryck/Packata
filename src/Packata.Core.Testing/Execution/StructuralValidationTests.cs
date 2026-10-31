using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Core.Testing;

public class StructuralValidationTests
{
    private static DataAsset Asset(bool allowAdditional = false, bool enforceOrder = true) =>
        new("orders", "orders", null, null, AssetKind.Table,
            new([new("a", "integer"), new("b", "string")], AllowAdditionalProperties: allowAdditional, EnforcePropertyOrder: enforceOrder));

    private static async Task<RuleEvaluationReport> Run(DataAsset asset, params DatasetStructure[] observations)
    {
        var compiler = new StructuralValidationCompiler(observations);
        return await RuleRunner.ExecuteAsync([compiler.Compile(asset, new())], new(DateTimeOffset.UnixEpoch));
    }

    [TestCase(false, "a,b", true)]
    [TestCase(false, "a,b,x", false)]
    [TestCase(false, "a", true)]
    [TestCase(false, "", true)]
    [TestCase(true, "a,b,x", true)]
    [TestCase(true, "x", true)]
    [TestCase(true, "", true)]
    public async Task Optional_presence_and_extra_permissions_are_independent(bool allowAdditional, string columns, bool passed)
    {
        var actual = columns.Length == 0 ? [] : columns.Split(',').Select(n => new ObservedColumn(n)).ToArray();
        var result = await Run(Asset(allowAdditional, false), new DatasetStructure("orders", "file.csv", actual));
        Assert.That(result.Passed, Is.EqualTo(passed));
        Assert.That(result.IsComplete, Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Required_presence_applies_to_all_boolean_combinations(bool allowAdditional, bool enforceOrder)
    {
        var asset = Asset(allowAdditional, enforceOrder);
        asset = asset with { Schema = asset.Schema! with { Fields = [new("a", "integer", Required: true, Nullable: true), new("b", "string")] } };
        var missing = await Run(asset, new DatasetStructure("orders", "file.csv", [new("b")]));
        Assert.That(missing.HasViolations, Is.True);
        Assert.That(missing.Results.Single(r => r.Descriptor.ExecutionId.Contains("propertyPresence")).Measurement.Evidence
            .Single(e => e.Name == "missingRequired").Value.Value, Is.EqualTo(new[] { QualityValue.From("a") }));
        Assert.That((await Run(asset, new DatasetStructure("orders", "file.csv", [new("a")]))).Passed, Is.True);
    }

    [TestCase(true, "a,x,b", true)]
    [TestCase(true, "b,x,a", false)]
    [TestCase(false, "b", true)]
    [TestCase(false, "b,a", false)]
    public async Task Declared_order_uses_present_properties_and_ignores_extras(bool allowAdditional, string columns, bool passed)
    {
        var result = await Run(Asset(allowAdditional), new DatasetStructure("orders", "file.csv", columns.Split(',').Select(n => new ObservedColumn(n)).ToArray()));
        Assert.That(result.Passed, Is.EqualTo(passed));
        Assert.That(result.Results.Single(r => r.Descriptor.ExecutionId.Contains("enforcePropertyOrder")).Outcome,
            Is.EqualTo(passed ? RuleOutcome.Passed : RuleOutcome.Failed));
    }

    [Test]
    public async Task Physical_names_case_sensitivity_duplicates_and_each_file_are_preserved()
    {
        var asset = Asset();
        asset = asset with { Schema = asset.Schema! with { Fields = [new("a", "integer", PhysicalName: "A"), new("b", "string")] } };
        var result = await Run(asset,
            new("orders", "good.csv", [new("A"), new("b")]),
            new("orders", "bad.csv", [new("a"), new("b")]),
            new("orders", "duplicate.csv", [new("A"), new("b"), new("b")]));
        Assert.That(result.Results.Where(r => r.Descriptor.SourcePath == "good.csv").All(r => r.Outcome == RuleOutcome.Passed), Is.True);
        Assert.That(result.Results.Single(r => r.Descriptor.SourcePath == "bad.csv" && r.Descriptor.ExecutionId.Contains("propertyPresence")).Outcome, Is.EqualTo(RuleOutcome.Failed));
        Assert.That(result.Results.Single(r => r.Descriptor.SourcePath == "duplicate.csv" && r.Descriptor.ExecutionId.Contains("propertyPresence")).Outcome, Is.EqualTo(RuleOutcome.Failed));
        Assert.That(result.Passed, Is.False);
    }

    [Test]
    public async Task Unavailable_observation_is_not_an_empty_schema_or_pass()
    {
        var result = await Run(Asset(), new DatasetStructure("orders", "empty.csv", null, DiagnosticCode: "HEADER_IDENTITY_UNAVAILABLE"));
        Assert.That(result.IsComplete, Is.False);
        Assert.That(result.Results.All(r => r.Outcome == RuleOutcome.Inconclusive), Is.True);
        Assert.That(result.Results.All(r => r.Measurement.Diagnostics.Single().Code == "HEADER_IDENTITY_UNAVAILABLE"), Is.True);
    }

    [Test]
    public async Task Read_errors_are_distinct_from_structural_violations()
    {
        var result = await Run(Asset(), new DatasetStructure("orders", "unreadable", null, ReadError: true));
        Assert.That(result.Results.All(r => r.Outcome == RuleOutcome.Error), Is.True);
        Assert.That(result.HasViolations, Is.False);
    }

    [TestCase("integer", RuleOutcome.Passed)]
    [TestCase("string", RuleOutcome.Failed)]
    [TestCase(null, RuleOutcome.Inconclusive)]
    public async Task Authoritative_schema_types_are_checked_without_parsing_cells(string? observed, RuleOutcome expected)
    {
        var result = await Run(Asset(), new DatasetStructure("orders", "input.parquet", [new("a", observed), new("b", "string")], TypesAvailable: true));
        Assert.That(result.Results.Single(r => r.Descriptor.Target.FieldPath.SequenceEqual(new[] { "a" })).Outcome, Is.EqualTo(expected));
    }

    [Test]
    public async Task Absent_optional_columns_have_no_schema_type_check()
    {
        var result = await Run(Asset(),
            new DatasetStructure("orders", "input.parquet", [new("a", "integer")], TypesAvailable: true));
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Results.Any(r => r.Descriptor.Target.FieldPath.Contains("b")), Is.False);
    }

    [Test]
    public async Task Relationship_structure_checks_columns_but_never_key_values()
    {
        var source = Asset() with { Schema = Asset().Schema! with { Relationships = [new(["a"], "customers", ["id"])] } };
        var target = new DataAsset("customers", "customers", null, null, AssetKind.Table, new([new("id", "integer")]));
        var contract = new DataContract(new("contract"), new(), [source, target], [], new());
        async Task<RuleResult> Relationship(params ObservedColumn[] targetColumns)
        {
            var compiler = new StructuralValidationCompiler([new("orders", "orders.csv", [new("a"), new("b")]), new("customers", "customers.csv", targetColumns)]);
            var report = await RuleRunner.ExecuteAsync([compiler.Compile(source, new(contract))], new(DateTimeOffset.UnixEpoch));
            return report.Results.Single(r => r.Descriptor.ExecutionId.Contains("relationship"));
        }
        Assert.That((await Relationship(new ObservedColumn("id"))).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That((await Relationship(new ObservedColumn("other"))).Outcome, Is.EqualTo(RuleOutcome.Failed));
    }

    [Test]
    public async Task Observations_are_snapshotted_and_shared_limits_are_honored()
    {
        var columns = new List<ObservedColumn> { new("a"), new("b") };
        var compiler = new StructuralValidationCompiler([new DatasetStructure("orders", "file.csv", columns)]);
        columns.Clear();
        var report = await RuleRunner.ExecuteAsync([compiler.Compile(Asset(), new())], new(DateTimeOffset.UnixEpoch, Limits: new(MaxEvidence: 1)));
        Assert.That(report.Passed, Is.True);
        Assert.That(report.Results.All(r => r.Measurement.Evidence.Count <= 1), Is.True);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await RuleRunner.ExecuteAsync([compiler.Compile(Asset(), new())], new(DateTimeOffset.UnixEpoch), cancelled.Token));
    }
}
