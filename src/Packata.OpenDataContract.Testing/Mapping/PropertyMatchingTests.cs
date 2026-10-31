using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class PropertyMatchingTests
{
    [TestCase(null, null, PropertyMatching.Equal, PropertyOrder.Declared)]
    [TestCase("subset", null, PropertyMatching.Subset, PropertyOrder.Declared)]
    [TestCase(null, "any", PropertyMatching.Equal, PropertyOrder.Any)]
    [TestCase("equal", "declared", PropertyMatching.Equal, PropertyOrder.Declared)]
    [TestCase("superset", "any", PropertyMatching.Superset, PropertyOrder.Any)]
    [TestCase("partial", "declared", PropertyMatching.Partial, PropertyOrder.Declared)]
    public void Maps_independent_settings_and_defaults(string? matching, string? order, PropertyMatching expected, PropertyOrder expectedOrder)
    {
        var schema = new SchemaObject { Name = "orders" };
        if (matching is not null) schema.CustomProperties.Add(Declaration("propertyMatching", matching));
        if (order is not null) schema.CustomProperties.Add(Declaration("propertyOrder", order));
        var result = new DataContract { Schema = [schema] }.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        var actual = result.RequireValue().Assets[0].Schema!;
        Assert.That(actual.PropertyMatching, Is.EqualTo(expected));
        Assert.That(actual.PropertyOrder, Is.EqualTo(expectedOrder));
        Assert.That(schema.CustomProperties.Count, Is.EqualTo((matching is null ? 0 : 1) + (order is null ? 0 : 1)));
    }

    [TestCase("propertyMatching", "exact")]
    [TestCase("propertyOrder", "equal")]
    [TestCase("propertyOrder", true)]
    [TestCase("propertyMatching", null)]
    public void Invalid_values_are_errors(string name, object? value)
    {
        var result = new DataContract { Schema = [new SchemaObject { Name = "orders", CustomProperties = [Declaration(name, value)] }] }.ToCanonicalContract();
        Assert.That(result.Diagnostics.Single().Severity, Is.EqualTo(MappingSeverity.Error));
        Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("schema/orders/customProperties/0"));
    }

    [Test]
    public void Duplicate_declarations_and_field_placement_are_errors()
    {
        var result = new DataContract { Schema = [new SchemaObject { Name = "orders",
            CustomProperties = [Declaration("propertyOrder", "any"), Declaration("propertyOrder", "declared")],
            Properties = [new SchemaProperty { Name = "id", CustomProperties = [Declaration("propertyMatching", "equal")] }]
        }] }.ToCanonicalContract();
        Assert.That(result.Diagnostics.Count, Is.EqualTo(3));
        Assert.That(result.Diagnostics.All(d => d.Code == "ODCS008" && d.Severity == MappingSeverity.Error), Is.True);
    }

    [Test]
    public void Other_vendors_are_preserved_without_changing_defaults()
    {
        var result = new DataContract { Schema = [new SchemaObject { Name = "orders",
            CustomProperties = [new() { Vendor = "other", Property = "propertyOrder", Value = "invalid" }]
        }] }.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.RequireValue().Assets[0].Schema!.PropertyOrder, Is.EqualTo(PropertyOrder.Declared));
        Assert.That(result.RequireValue().Assets[0].Extensions.Namespaces, Does.Contain("odcs"));
    }

    private static CustomProperty Declaration(string name, object? value) => new() { Vendor = "packata", Property = name, Value = value };
}
