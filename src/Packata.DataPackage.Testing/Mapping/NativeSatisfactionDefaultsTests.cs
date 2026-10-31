using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;

namespace Packata.DataPackage.Testing.Mapping;

public class NativeSatisfactionDefaultsTests
{
    [TestCase(FieldsMatching.Exact, false, true, true)]
    [TestCase(FieldsMatching.Equal, false, false, true)]
    [TestCase(FieldsMatching.Subset, true, false, true)]
    [TestCase(FieldsMatching.Superset, false, false, false)]
    [TestCase(FieldsMatching.Partial, true, false, false)]
    public void Field_matching_guarantees_are_separate_from_nullable_values(FieldsMatching native, bool normalized, bool order, bool required)
    {
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { FieldsMatch = native, Fields = [new StringField { Name = "id", Type = "string" }] }
        }] };
        var schema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(schema.AllowAdditionalProperties, Is.EqualTo(normalized));
        Assert.That(schema.EnforcePropertyOrder, Is.EqualTo(order));
        Assert.That(schema.Fields[0].Required, Is.EqualTo(required));
        Assert.That(schema.Fields[0].Nullable, Is.True);
    }

    [Test]
    public void Omitted_matching_defaults_to_exact_without_forcing_nonnull_values()
    {
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { Fields = [new StringField { Name = "id", Type = "string" }] }
        }] };
        var schema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(schema.AllowAdditionalProperties, Is.False);
        Assert.That(schema.EnforcePropertyOrder, Is.True);
        Assert.That(schema.Fields[0].Required, Is.True);
        Assert.That(schema.Fields[0].Nullable, Is.True);
    }

    [TestCase(FieldsMatching.Superset)]
    [TestCase(FieldsMatching.Partial)]
    public void Native_key_presence_and_cell_requiredness_are_kept_separate(FieldsMatching mode)
    {
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { FieldsMatch = mode, PrimaryKey = ["id"],
                Fields = [new StringField { Name = "id", Type = "string" }] }
        }] };
        var schema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(schema.Fields[0].Required, Is.True);
        Assert.That(schema.Extensions["datapackage"]["fieldsMatch"], Is.EqualTo(mode.ToString()));
    }

    [TestCase(FieldsMatching.Exact, true)]
    [TestCase(FieldsMatching.Superset, false)]
    [TestCase(FieldsMatching.Partial, false)]
    public void Required_cell_values_do_not_replace_fields_match_presence(FieldsMatching mode, bool presenceRequired)
    {
        var constraints = new FieldConstraintCollection();
        constraints.Add(new RequiredConstraint(true));
        var package = new DataPackage { Name = "sales", Resources = [new Resource
        {
            Name = "orders", Schema = new Schema { FieldsMatch = mode,
                Fields = [new StringField { Name = "id", Type = "string", Constraints = constraints }] }
        }] };
        var field = package.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.Required, Is.EqualTo(presenceRequired));
        Assert.That(field.Nullable, Is.False);
    }
}
