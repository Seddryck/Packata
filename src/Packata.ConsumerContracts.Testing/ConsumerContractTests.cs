using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.ConsumerContracts.Testing;

public class ConsumerContractTests
{
    internal static DataContract Producer(params DataField[] fields) => new(new("sales"), new(),
        [new("orders", "orders", null, null, AssetKind.Table, new(fields))], [], new());

    [TestCase(false)]
    [TestCase(true)]
    public void Serialization_preserves_fluent_requirements(bool json)
    {
        ConsumerContract consumer = ConsumerContract.For("report").Version("1.0").Target("sales")
            .Dataset("orders", d => d.Field("id", f => f.Type(LogicalType.String)).Field("amount"))
            .Dataset("customers");
        var text = ConsumerContractSerializer.Serialize(consumer, json);
        var copy = ConsumerContractSerializer.Deserialize(text, json);
        Assert.That(ConsumerContractSerializer.Serialize(copy, json), Is.EqualTo(text));
        Assert.That(copy.Datasets[0].Fields[1].Type, Is.Null);
    }

    [Test]
    public void Evaluation_reports_all_failures_and_ignores_extra_producer_fields()
    {
        ConsumerContract consumer = ConsumerContract.For("report").Dataset("orders", d => d
            .Field("id", f => f.Type(LogicalType.String)).Field("missing")
            .Field("amount", f => f.Type(LogicalType.Decimal))).Dataset("absent", d => d.Field("id"));
        var result = Producer(new("id", "string"), new("amount", "integer"), new("extra", "boolean")).Satisfies(consumer);
        Assert.That(result.Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
        Assert.That(result.Results.Single(x => x.Field == "missing").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
        Assert.That(result.Results.Single(x => x.Field == "amount" && x.Constraint == "type").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
        Assert.That(result.Results.Single(x => x.Dataset == "absent" && x.Field == "id").Status, Is.EqualTo(SatisfactionStatus.Unknown));
    }

    [Test]
    public void Missing_type_and_wrong_target_do_not_prove_satisfaction()
    {
        ConsumerContract consumer = ConsumerContract.For("report").Dataset("orders", d => d.Field("id", f => f.Type(LogicalType.String)));
        Assert.That(Producer(new DataField("id", null)).Satisfies(consumer).Status, Is.EqualTo(SatisfactionStatus.Unknown));
        var wrongTarget = consumer with { Target = new("other") };
        var result = Producer(new DataField("id", "string")).Satisfies(wrongTarget);
        Assert.That(result.IsSatisfied, Is.False);
        Assert.That(result.Results.Where(x => x.Constraint != "target").All(x => x.Status == SatisfactionStatus.Unknown), Is.True);
    }

    [Test]
    public void Invalid_requirements_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ConsumerContract.For("report").Build());
        Assert.Throws<ArgumentException>(() => ConsumerContract.For("report").Dataset("orders", d => d.Field("id").Field("id")).Build());
        Assert.Throws<ArgumentException>(() => ConsumerContractSerializer.Deserialize("apiVersion: v2\nkind: ConsumerContract\nid: report\ndatasets: []"));
    }
}

