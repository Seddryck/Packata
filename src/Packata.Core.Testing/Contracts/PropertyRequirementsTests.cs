using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class PropertyRequirementsTests
{
    private static DataContract Contract(bool allowAdditional, bool enforceOrder, params string[] names) =>
        new(new("orders"), new(), [new("orders", "orders", null, null, AssetKind.Table,
            new(names.Select(n => new DataField(n, "string", Required: true)).ToArray(), AllowAdditionalProperties: allowAdditional, EnforcePropertyOrder: enforceOrder))], [], new());

    [Test]
    public void Diff_retains_independent_settings_and_declared_sequence_changes()
    {
        var before = Contract(false, true, "a", "b");
        Assert.That(before.Diff(Contract(true, true, "a", "b")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
        Assert.That(before.Diff(Contract(false, false, "a", "b")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
        Assert.That(before.Diff(Contract(false, true, "b", "a")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
        Assert.That(Contract(false, false, "a").Diff(Contract(false, false, "a", "b"))
            .Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Satisfaction_checks_relative_order_independently_of_membership()
    {
        var consumer = Contract(true, true, "a", "b");
        var producer = Contract(false, true, "a", "x", "b");
        var report = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(report.IsSatisfied, Is.True);
        producer = Contract(false, true, "b", "x", "a");
        report = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(report.Findings.Single(f => f.Code == "SAT104").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
    }

    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void Satisfaction_compares_additional_property_guarantees(bool producerExtras, bool consumerExtras, bool satisfied)
    {
        var report = ContractSatisfactionEvaluator.EvaluateSatisfaction(
            Contract(producerExtras, false, "a"), Contract(consumerExtras, false, "a"), SatisfactionPolicy.Default);
        Assert.That(report.IsSatisfied, Is.EqualTo(satisfied));
    }

    [Test]
    public void Direct_construction_and_partial_native_restrictions_are_explicit()
    {
        var schema = new DataSchema([]);
        Assert.That(schema.AllowAdditionalProperties, Is.True);
        Assert.That(schema.EnforcePropertyOrder, Is.False);
        var contract = Contract(true, false, "a");
        var asset = contract.Assets[0];
        contract = contract with { Assets = [asset with { Schema = asset.Schema! with
            { Extensions = ExtensionMetadata.For("datapackage", new Dictionary<string, object?> { ["fieldsMatch"] = "Partial" }) } }] };
        var report = ContractSatisfactionEvaluator.EvaluateSatisfaction(contract, contract, SatisfactionPolicy.Default);
        Assert.That(report.Findings.Any(f => f.Code == "SAT026" && f.Status == SatisfactionStatus.Unknown), Is.True);
        Assert.That(report.IsSatisfied, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Record_export_keeps_extra_permission_and_independent_required_properties(bool allowAdditional)
    {
        var asset = Contract(allowAdditional, true, "a", "b").Assets[0];
        asset = asset with { Schema = asset.Schema! with { Fields = [new("a", "string", Required: true, Nullable: true), new("b", "string")] } };
        var exported = asset.ToJsonSchema();
        Assert.That(exported["additionalProperties"], Is.EqualTo(allowAdditional));
        Assert.That(exported["required"], Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void Record_export_preserves_native_partial_overlap()
    {
        var asset = Contract(true, false, "a", "b").Assets[0];
        asset = asset with { Schema = asset.Schema! with { Fields = [new("a", "string"), new("b", "string")],
            Extensions = ExtensionMetadata.For("datapackage", new Dictionary<string, object?> { ["fieldsMatch"] = "Partial" }) } };
        var exported = asset.ToJsonSchema();
        var alternatives = (Dictionary<string, object?>[])exported["anyOf"]!;
        Assert.That(alternatives.Select(x => ((string[])x["required"]!)[0]), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(exported.ContainsKey("required"), Is.False);
    }
}
