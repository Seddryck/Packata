using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;

namespace Packata.DataPackage.Testing.Mapping;

public class NativeSatisfactionDefaultsTests
{
    [TestCase(FieldsMatching.Exact, FieldMatching.OrderedExact, true)]
    [TestCase(FieldsMatching.Equal, FieldMatching.Exact, true)]
    [TestCase(FieldsMatching.Subset, FieldMatching.ByName, true)]
    [TestCase(FieldsMatching.Superset, FieldMatching.ClosedOptional, false)]
    [TestCase(FieldsMatching.Partial, FieldMatching.Partial, false)]
    public void Field_matching_guarantees_are_separate_from_nullable_values(FieldsMatching native, FieldMatching normalized, bool required)
    {
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { FieldsMatch = native, Fields = [new StringField { Name = "id", Type = "string" }] }
        }] };
        var schema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(schema.FieldMatching, Is.EqualTo(normalized));
        Assert.That(schema.Fields[0].Required, Is.EqualTo(required));
        Assert.That(schema.Fields[0].Nullable, Is.True);
    }
}
