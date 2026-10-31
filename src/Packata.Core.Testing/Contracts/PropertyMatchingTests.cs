using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing;

public class PropertyMatchingTests
{
    private static DataContract Contract(PropertyMatching matching, PropertyOrder order, params string[] names) =>
        new(new("orders"), new(), [new("orders", "orders", null, null, AssetKind.Table,
            new(names.Select(n => new DataField(n, "string")).ToArray(), PropertyMatching: matching, PropertyOrder: order))], [], new());

    [Test]
    public void Diff_retains_independent_settings_and_declared_sequence_changes()
    {
        var before = Contract(PropertyMatching.Equal, PropertyOrder.Declared, "a", "b");
        Assert.That(before.Diff(Contract(PropertyMatching.Subset, PropertyOrder.Declared, "a", "b")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
        Assert.That(before.Diff(Contract(PropertyMatching.Equal, PropertyOrder.Any, "a", "b")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
        Assert.That(before.Diff(Contract(PropertyMatching.Equal, PropertyOrder.Declared, "b", "a")).Changes.OfType<DatasetModified>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Satisfaction_checks_relative_order_independently_of_membership()
    {
        var consumer = Contract(PropertyMatching.Subset, PropertyOrder.Declared, "a", "b");
        var producer = Contract(PropertyMatching.Equal, PropertyOrder.Declared, "a", "x", "b");
        var report = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(report.IsSatisfied, Is.True);
        producer = Contract(PropertyMatching.Equal, PropertyOrder.Declared, "b", "x", "a");
        report = ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, SatisfactionPolicy.Default);
        Assert.That(report.Findings.Single(f => f.Code == "SAT104").Status, Is.EqualTo(SatisfactionStatus.Unsatisfied));
    }
}
