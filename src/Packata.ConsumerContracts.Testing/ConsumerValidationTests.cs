using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.ConsumerContracts.Testing;

public class ConsumerValidationTests
{
    [TestCase("id: report\ndatasets:\n  - name: orders")]
    [TestCase("apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets: null")]
    [TestCase("apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets:\n  - name: orders\n    fields: null")]
    [TestCase("apiVersion: v1\nkind: ConsumerContract\nid: report\ndatasets:\n  - name: orders\n    fields:\n      - null")]
    public void Invalid_serialized_structure_is_rejected(string yaml)
    {
        Assert.Throws<ArgumentException>(() => ConsumerContractSerializer.Deserialize(yaml));
    }

    [Test]
    public void Inferred_builder_type_can_be_evaluated_and_serialized()
    {
        var consumer = ConsumerContract.For("report").Dataset("orders");
        var result = ConsumerContractTests.Producer().Satisfies(consumer);
        Assert.That(result.IsSatisfied, Is.True);
        Assert.That(ConsumerContractSerializer.Serialize(consumer), Does.Contain("report"));
    }
}
