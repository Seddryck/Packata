using System.Data;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Core.Testing.Execution;

public class ConstraintCompilerTests
{
    [Test]
    public async Task Invalid_cells_are_counted_and_preserved_for_requiredness_and_other_columns()
    {
        var asset = Asset("orders", [new("id", "integer", Required: true), new("day", "date", Format: "dd/MM/yyyy"),
            new("instant", "timestamp", Format: "yyyy/MM/dd HH:mm:sszzz"),
            new("name", "string", Constraints: [new("minLength", 2)])]);
        var raw = new Rows(["id", "day", "instant", "name"],
            ["two", "29/02/2023", "2024-03-01T08:00:00Z", "A"],
            ["2.50", "2024-02-29", "2024/03/01 08:00:00+00:00", "🦉"],
            ["2.00", "29/02/2024", "2024/03/01 08:00:00+00:00", "Bz"],
            [" 2", "29/02/2024", "2024/03/01 08:00:00+00:00", "Cy"],
            [null, null, null, "Cy"],
            ["+2", "29/02/2024", "2024/03/01 08:00:00+00:00", "Dz"]);
        var report = await Run([Input(asset, raw)], limits: new(MaxEvidence: 1));
        Assert.Multiple(() =>
        {
            Assert.That(Finding(report, ":id:logicalType").Outcome, Is.EqualTo(RuleOutcome.Failed));
            Assert.That(Count(report, ":id:logicalType"), Is.EqualTo(4));
            Assert.That(Count(report, ":id:required"), Is.EqualTo(5));
            Assert.That(Count(report, ":day:logicalType"), Is.EqualTo(2));
            Assert.That(Count(report, ":instant:logicalType"), Is.EqualTo(1));
            Assert.That(Count(report, ":name:minLength:0"), Is.EqualTo(2));
            Assert.That(Finding(report, ":id:logicalType").Measurement.Evidence, Has.Count.EqualTo(1));
            Assert.That(raw.Opens, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DataPackage_missing_tokens_boolean_options_and_typed_enum_are_honored()
    {
        var schemaOptions = ExtensionMetadata.For("datapackage", new Dictionary<string, object?> { ["missingValues"] = new[] { "-" } });
        var boolOptions = ExtensionMetadata.For("datapackage", new Dictionary<string, object?>
        {
            ["trueValues"] = new[] { "yes" },
            ["falseValues"] = new[] { "no" }
        });
        var integer = ScalarValue.From(2);
        var number = ScalarValue.From(2m);
        var domain = new ValueDomain([new(integer), new(number, "Two"), new(ScalarValue.From(3))], [number]);
        var asset = Asset("orders", [new("amount", "number", Nullable: true, ValueDomain: domain,
            Constraints: [new("minimum", 2), new("exclusiveMaximum", 3)]),
            new("paid", "boolean", Nullable: true, Extensions: boolOptions)], extensions: schemaOptions);
        var report = await Run([Input(asset, new Rows(["amount", "paid"],
            ["-", "-"], ["2", "yes"], ["2.00", "no"], ["3", "true"], ["1,25", "yes"], ["1", "no"]))]);
        Assert.Multiple(() =>
        {
            Assert.That(Count(report, ":amount:logicalType"), Is.EqualTo(1));
            Assert.That(Count(report, ":amount:minimum:0"), Is.EqualTo(1));
            Assert.That(Count(report, ":amount:exclusiveMaximum:1"), Is.EqualTo(1));
            Assert.That(Count(report, ":amount:enum:2"), Is.EqualTo(3));
            Assert.That(Count(report, ":paid:logicalType"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Incomplete_scan_retains_partial_counts_and_cannot_pass()
    {
        var asset = Asset("orders", [new("id", "integer")]);
        var report = await Run([Input(asset, new Rows(["id"], ["bad"], ["2"], ["3"]))], limits: new(MaxRows: 2));
        var result = Finding(report, ":logicalType");
        Assert.Multiple(() =>
        {
            Assert.That(report.Passed, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
            Assert.That(result.Measurement.Population, Is.EqualTo(2));
            Assert.That(result.Measurement.Value!.ToObject(), Is.EqualTo(1L));
            Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo("ROW_BUDGET"));
        });
    }

    [Test]
    public async Task Missing_field_and_unsupported_declarations_are_inconclusive()
    {
        var reference = new ValueDomain(reference: new("unbound"));
        var asset = Asset("orders", [new("id", "integer", Required: true),
            new("name", "string", Constraints: [new("pattern", "[")], ValueDomain: reference),
            new("doc", "object", Constraints: [new("jsonSchema", "{}")])]);
        var report = await Run([Input(asset, new Rows(["name", "doc"], ["a", "{}"]))]);
        Assert.Multiple(() =>
        {
            Assert.That(report.HasViolations, Is.False);
            Assert.That(Finding(report, ":id:required").Measurement.Diagnostics.Single().Code, Is.EqualTo("CONSTRAINT_COLUMN_UNAVAILABLE"));
            Assert.That(Finding(report, ":pattern:0").Measurement.Diagnostics.Single().Code, Is.EqualTo("CONSTRAINT_OPERAND_INVALID"));
            Assert.That(Finding(report, ":category").Measurement.Diagnostics.Single().Code, Is.EqualTo("CATEGORY_REFERENCE_UNAVAILABLE"));
            Assert.That(Finding(report, ":jsonSchema:0").Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        });
    }

    [Test]
    public async Task Foreign_keys_use_ordered_tuples_and_target_uniqueness_is_independent()
    {
        DataField[] fields = [new("a", "integer"), new("b", "integer")];
        var source = Asset("orders", fields, relationships: [new(["a", "b"], "customers", ["b", "a"])]);
        var target = Asset("customers", fields, primary: ["a", "b"]);
        var targets = Input(target, new Rows(["a", "b"], ["1", "2"], ["1", "2"], ["3", "4"]));
        var orders = Input(source, new Rows(["a", "b"], ["2", "1"], ["1", "2"], ["2", "3"], [null, "1"], [null, null]));
        var report = await Run([orders, targets], [new("orders", 0)]);
        var foreign = Finding(report, ":foreignKey:0");
        Assert.Multiple(() =>
        {
            Assert.That(Count(report, ":primaryKey"), Is.EqualTo(1));
            Assert.That(foreign.Outcome, Is.EqualTo(RuleOutcome.Failed));
            Assert.That(foreign.Measurement.Value!.ToObject(), Is.EqualTo(2L));
            Assert.That(foreign.Measurement.Evidence.Select(item => ((Dictionary<string, object?>)item.Value.ToObject()!)["record"]),
                Is.EqualTo(new long[] { 2, 3 }));
        });
    }

    [TestCase("FOREIGN_KEY_TARGET_UNAVAILABLE")]
    [TestCase("ROW_BUDGET")]
    [TestCase("RELATIONSHIP_COLUMN_UNAVAILABLE")]
    [TestCase("KEY_STATE_BUDGET")]
    public async Task Blocked_foreign_key_lookup_never_reports_fabricated_orphans(string expected)
    {
        var source = Asset("orders", [new("id", "integer")], relationships: [new(["id"], "customers", ["id"])]);
        var target = Asset("customers", [new("id", "integer")]);
        var sourceInput = Input(source, new Rows(["id"], ["42"]));
        var targetInput = Input(target, new Rows(["id"], ["1"], ["2"]));
        ConstraintInput[] inputs = expected == "FOREIGN_KEY_TARGET_UNAVAILABLE" ? [sourceInput] : [sourceInput, targetInput];
        var report = await Run(inputs, [new("orders", 0, expected == "RELATIONSHIP_COLUMN_UNAVAILABLE" ? expected : null)],
            new(MaxRows: expected == "ROW_BUDGET" ? 1 : 10, MaxStateEntries: expected == "KEY_STATE_BUDGET" ? 1 : 10));
        var result = Finding(report, ":foreignKey:0");
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
            Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo(expected));
            Assert.That(result.Measurement.Value, Is.Null);
        });
    }

    [Test]
    public async Task Same_asset_foreign_keys_reuse_typed_values_and_exempt_null_sources()
    {
        var asset = Asset("people", [new("id", "integer"), new("parent", "integer", Nullable: true)],
            relationships: [new(["parent"], "people", ["id"])]);
        var raw = new Rows(["id", "parent"], ["1", null], ["2", "1"], ["3", "4"]);
        var report = await Run([Input(asset, raw)], [new("people", 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(Count(report, ":foreignKey:0"), Is.EqualTo(1));
            Assert.That(raw.Opens, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Referenced_labels_can_only_encode_values_in_the_effective_subset()
    {
        var domain = new ValueDomain(allowedValues: [ScalarValue.From(1)], reference: new("codes", encodedAs: ValueEncoding.Label, boundAssetId: "codes"));
        var source = Asset("orders", [new("code", "string", ValueDomain: domain)]);
        var codes = Asset("codes", [new("value", "integer"), new("label", "string")]) with
        {
            CategoryTable = new(new("value"), new("label"))
        };
        var report = await Run([Input(source, new Rows(["code"], ["One"], ["Two"], ["1"])),
            Input(codes, new Rows(["value", "label"], ["1", "One"], ["2", "Two"]))]);
        Assert.That(Count(report, ":category"), Is.EqualTo(2));
    }

    [Test]
    public async Task Unsupported_parsing_does_not_report_present_values_as_null()
    {
        var asset = Asset("objects", [new("doc", "object", Required: true)]);
        var report = await Run([Input(asset, new Rows(["doc"], ["{}"]))]);
        Assert.Multiple(() =>
        {
            Assert.That(Finding(report, ":logicalType").Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
            Assert.That(Finding(report, ":required").Outcome, Is.EqualTo(RuleOutcome.Passed));
            Assert.That(report.Passed, Is.False);
        });
    }

    [Test]
    public async Task DataPackage_formats_require_declared_fraction_and_offset()
    {
        var asset = Asset("events", [new("instant", "datetime", Format: "%Y-%m-%dT%H:%M:%S.%f%z", Nullable: true)],
            extensions: ExtensionMetadata.For("datapackage", new Dictionary<string, object?>()));
        var report = await Run([Input(asset, new Rows(["instant"], ["2024-02-29T13:30:00.123Z"], ["2024-02-29T13:30:00.1+0000"],
            ["2024-02-29T13:30:00.1"], ["2024-02-29T13:30:00.Z"], ["2024-02-30T13:30:00.1Z"]))]);
        Assert.That(Count(report, ":logicalType"), Is.EqualTo(3));
    }

    private static DataAsset Asset(string id, DataField[] fields, string[]? primary = null, DataRelationship[]? relationships = null,
        ExtensionMetadata? extensions = null)
        => new(id, id, null, null, AssetKind.Table, new(fields, primary, relationships, extensions));

    private static ConstraintInput Input(DataAsset asset, Rows rows)
        => new(asset, $"{asset.Id}.csv", rows.Names.ToDictionary(name => name, name => name), rows);

    private static ValueTask<RuleEvaluationReport> Run(ConstraintInput[] inputs,
        ConstraintRelationshipPrerequisite[]? relationships = null, RuleLimits? limits = null)
    {
        var compiler = new ConstraintCompiler(inputs, relationships);
        return RuleRunner.ExecuteAsync(inputs.Select(input => input.Asset).Distinct().Select(asset => compiler.Compile(asset, new())),
            new(DateTimeOffset.UnixEpoch, Limits: limits) { ConstraintInput = new() });
    }

    private static RuleResult Finding(RuleEvaluationReport report, string suffix)
        => report.Results.Single(result => result.Descriptor.ExecutionId.EndsWith(suffix, StringComparison.Ordinal));

    private static object? Count(RuleEvaluationReport report, string suffix)
        => Finding(report, suffix).Measurement.Value!.ToObject();

    private sealed class Rows(string[] names, params object?[][] rows) : IRuleDataSource
    {
        public string[] Names { get; } = names;
        public int Opens { get; private set; }
        public string SnapshotId => "fixture";
        public bool IsReplayable => false;
        public bool IsComplete => true;

        public ValueTask<IDataReader> OpenAsync(QualityTarget target, string? endpointId, CancellationToken cancellationToken)
        {
            Opens++;
            var table = new DataTable();
            foreach (var name in Names) table.Columns.Add(name, typeof(object));
            foreach (var row in rows) table.Rows.Add(row);
            return ValueTask.FromResult<IDataReader>(table.CreateDataReader());
        }
    }
}
