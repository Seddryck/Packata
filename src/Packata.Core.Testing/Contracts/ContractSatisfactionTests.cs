using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class ContractSatisfactionTests
{
    internal static DataContract Contract(params DataField[] fields) => new(new("independent"), new(),
        [new("orders", "orders", "physical", null, AssetKind.Table, new(fields))], [], new());
    private static ContractSatisfaction Evaluate(DataField producer, DataField consumer) =>
        ContractSatisfactionEvaluator.EvaluateSatisfaction(Contract(producer), Contract(consumer), SatisfactionPolicy.Default);
    private static SatisfactionFinding Finding(ContractSatisfaction result, string code) => result.Findings.Single(x => x.Code == code);

    [Test]
    public void Independent_identities_are_informational_and_inputs_are_not_modified()
    {
        var producer = Contract(new DataField("id", "string", Required: true)) with { Identity = new("producer", Version: "2") };
        var consumer = producer with { Identity = new("consumer", Version: "1") };
        var before = producer;
        var result = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(result.IsSatisfied, Is.True);
        Assert.That(result.Producer.Identity!.Id, Is.EqualTo("producer"));
        Assert.That(result.Consumer.Identity!.Id, Is.EqualTo("consumer"));
        Assert.That(producer, Is.SameAs(before));
        Assert.That(result.Findings.Any(x => x.Status == SatisfactionStatus.Informational), Is.True);
    }

    [TestCase("integer", "number", SatisfactionStatus.Satisfied)]
    [TestCase("number", "integer", SatisfactionStatus.Unsatisfied)]
    [TestCase("string", "string", SatisfactionStatus.Satisfied)]
    [TestCase("geography", "geography", SatisfactionStatus.Unknown)]
    public void Type_containment_is_directional(string producer, string consumer, SatisfactionStatus status)
        => Assert.That(Finding(Evaluate(new("id", producer), new("id", consumer)), "SAT110").Status, Is.EqualTo(status));

    [TestCase(false, false, SatisfactionStatus.Satisfied)]
    [TestCase(false, true, SatisfactionStatus.Unsatisfied)]
    [TestCase(true, false, SatisfactionStatus.Satisfied)]
    [TestCase(true, true, SatisfactionStatus.Satisfied)]
    public void Nullability_is_independent_of_type(bool consumer, bool producer, SatisfactionStatus status)
    {
        var result = Evaluate(new("id", "boolean", Nullable: producer), new("id", "string", Nullable: consumer));
        Assert.That(Finding(result, "SAT112").Status, Is.EqualTo(status));
        Assert.That(Finding(result, "SAT110").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
        Assert.That(Finding(result, "SAT120:maxLength").Status, Is.EqualTo(SatisfactionStatus.Unknown));
    }

    [TestCase(30, 50, SatisfactionStatus.Satisfied)]
    [TestCase(100, 50, SatisfactionStatus.Unsatisfied)]
    [TestCase(null, 50, SatisfactionStatus.Unsatisfied)]
    [TestCase(100, null, SatisfactionStatus.Satisfied)]
    [TestCase(null, null, SatisfactionStatus.Satisfied)]
    public void Native_length_omissions_are_unbounded(int? producer, int? consumer, SatisfactionStatus status)
    {
        static DataField Field(int? length) => new("id", "string", Constraints: length is null ? [] : [new("maxLength", length)]);
        Assert.That(Finding(Evaluate(Field(producer), Field(consumer)), "SAT120:maxLength").Status, Is.EqualTo(status));
    }

    [TestCase(10, 2, 12, 4, SatisfactionStatus.Satisfied)]
    [TestCase(12, 2, 12, 4, SatisfactionStatus.Unsatisfied)]
    [TestCase(12, 4, 10, 2, SatisfactionStatus.Unsatisfied)]
    public void Decimal_checks_fractional_and_integral_capacity(int pp, int ps, int cp, int cs, SatisfactionStatus status)
    {
        static DataField Field(int p, int s) => new("amount", "number", Constraints: [new("precision", p), new("scale", s)]);
        var result = Evaluate(Field(pp, ps), Field(cp, cs));
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Findings.Count(x => x.Code.StartsWith("SAT121:", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public void Missing_optional_field_is_accepted_and_presence_is_distinct_from_nullability()
    {
        var producer = Contract() with { Assets = [Contract().Assets[0] with { Schema = new([], AllowAdditionalProperties: false) }] };
        var consumer = Contract(new DataField("id", "string"));
        consumer = consumer with { Assets = [consumer.Assets[0] with { Schema = consumer.Assets[0].Schema! with { AllowAdditionalProperties = false } }] };
        var result = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(result.IsSatisfied, Is.True);
        var required = ContractSatisfactionEvaluator.EvaluateSatisfaction(Contract(), Contract(new DataField("id", "string", Required: true)), SatisfactionPolicy.Default);
        Assert.That(required.IsSatisfied, Is.False);
        Assert.That(Finding(required, "SAT101").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
        Assert.That(Finding(required, "SAT110").Status, Is.EqualTo(SatisfactionStatus.Unknown));
    }

    [Test]
    public void Missing_dataset_extra_fields_nested_items_and_strict_policy_are_visible()
    {
        var field = new DataField("object", "object", Children: [new("a.b", "array", Items: new("item", "object", Children: [new("x", "integer", Required: true)]))]);
        var producer = Contract(field, new("extra", "boolean"));
        var consumer = Contract(field);
        var result = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(result.IsSatisfied, Is.True);
        Assert.That(result.Findings.Any(x => x.ConsumerPath.Contains("a\\.b.items.properties.x", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Findings.Any(x => x.Code == "SAT102"), Is.True);
        Assert.That(ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, new(true)).IsSatisfied, Is.False);
        var missing = producer with { Assets = [] };
        Assert.That(ContractSatisfactionEvaluator.EvaluateSatisfaction(missing, consumer, SatisfactionPolicy.Default).IsSatisfied, Is.False);
    }

    [Test]
    public void Identical_unsupported_semantics_remain_unknown()
    {
        var field = new DataField("id", "integer", QualityRules: [new("sql", Expression: "x > 0")], Constraints: [new("custom", 1)]);
        var result = Evaluate(field, field);
        Assert.That(result.Completed, Is.True);
        Assert.That(result.IsSatisfied, Is.False);
        Assert.That(result.Findings.Any(x => x.Status == SatisfactionStatus.Unknown), Is.True);
    }

    [Test]
    public void Invalid_identifiers_cross_standard_and_cancellation_do_not_succeed()
    {
        var contract = Contract(new DataField("id", "string"), new("id", "integer"));
        Assert.That(ContractSatisfactionEvaluator.EvaluateSatisfaction(contract, contract, SatisfactionPolicy.Default).Completed, Is.False);
        var valid = Contract();
        var cross = ContractSatisfactionEvaluator.EvaluateSatisfaction(new SatisfactionInput(valid, ContractStandard.Odcs),
            new SatisfactionInput(valid, ContractStandard.DataPackage), SatisfactionPolicy.Default);
        Assert.That(cross.Completed, Is.False);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = ContractSatisfactionEvaluator.EvaluateSatisfaction(valid, valid, SatisfactionPolicy.Default, cancellation.Token);
        Assert.That(cancelled.Completed, Is.False);
        Assert.That(cancelled.Diagnostics.Any(x => x.Code == "SAT130"), Is.True);
    }
    [Test]
    public void Custom_namespace_metadata_is_unresolved_even_with_familiar_property_names()
    {
        var field = new DataField("id", "integer", Extensions: ExtensionMetadata.For("custom-semantics",
            new Dictionary<string, object?> { ["title"] = "This custom namespace has undefined semantics." }));
        Assert.That(Evaluate(field, field).IsSatisfied, Is.False);
    }
}