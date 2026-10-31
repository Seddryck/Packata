using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;

namespace Packata.DataPackage.Testing.Mapping;

public class NativeSatisfactionDefaultsTests
{
    [TestCase(FieldsMatching.Exact, PropertyMatching.Equal, PropertyOrder.Declared, true)]
    [TestCase(FieldsMatching.Equal, PropertyMatching.Equal, PropertyOrder.Any, true)]
    [TestCase(FieldsMatching.Subset, PropertyMatching.Subset, PropertyOrder.Any, true)]
    [TestCase(FieldsMatching.Superset, PropertyMatching.Superset, PropertyOrder.Any, false)]
    [TestCase(FieldsMatching.Partial, PropertyMatching.Partial, PropertyOrder.Any, false)]
    public void Field_matching_guarantees_are_separate_from_nullable_values(FieldsMatching native, PropertyMatching normalized, PropertyOrder order, bool required)
    {
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { FieldsMatch = native, Fields = [new StringField { Name = "id", Type = "string" }] }
        }] };
        var schema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(schema.PropertyMatching, Is.EqualTo(normalized));
        Assert.That(schema.PropertyOrder, Is.EqualTo(order));
        Assert.That(schema.Fields[0].Required, Is.EqualTo(required));
        Assert.That(schema.Fields[0].Nullable, Is.True);
    }
}
