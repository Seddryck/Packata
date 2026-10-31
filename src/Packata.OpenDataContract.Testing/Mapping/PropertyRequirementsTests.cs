using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class PropertyRequirementsTests
{
    [TestCase(null, null, false, true)]
    [TestCase(true, null, true, true)]
    [TestCase(null, false, false, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, false, false, false)]
    [TestCase(true, true, true, true)]
    [TestCase(true, false, true, false)]
    public void Maps_independent_settings_and_defaults(bool? additional, bool? order, bool expected, bool expectedOrder)
    {
        var schema = new SchemaObject { Name = "orders",
            Properties = [new() { Name = "optional" }, new() { Name = "required", Required = true, Nullable = true }] };
        if (additional is not null) schema.CustomProperties.Add(Declaration("allowAdditionalProperties", additional.Value));
        if (order is not null) schema.CustomProperties.Add(Declaration("enforcePropertyOrder", order.Value));
        var result = new DataContract { Schema = [schema] }.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        var actual = result.RequireValue().Assets[0].Schema!;
        Assert.That(actual.AllowAdditionalProperties, Is.EqualTo(expected));
        Assert.That(actual.EnforcePropertyOrder, Is.EqualTo(expectedOrder));
        Assert.That(actual.Fields[0].Required, Is.False);
        Assert.That(actual.Fields[1].Required, Is.True);
        Assert.That(actual.Fields[1].Nullable, Is.True);
        Assert.That(schema.CustomProperties.Count, Is.EqualTo((additional is null ? 0 : 1) + (order is null ? 0 : 1)));
    }

    [TestCase("allowAdditionalProperties", "true")]
    [TestCase("enforcePropertyOrder", "false")]
    [TestCase("enforcePropertyOrder", 1)]
    [TestCase("allowAdditionalProperties", null)]
    [TestCase("propertyMatching", "equal")]
    [TestCase("propertyOrder", "declared")]
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
            CustomProperties = [Declaration("enforcePropertyOrder", false), Declaration("enforcePropertyOrder", true)],
            Properties = [new SchemaProperty { Name = "id", CustomProperties = [Declaration("allowAdditionalProperties", false)] }]
        }] }.ToCanonicalContract();
        Assert.That(result.Diagnostics.Count, Is.EqualTo(3));
        Assert.That(result.Diagnostics.All(d => d.Code == "ODCS008" && d.Severity == MappingSeverity.Error), Is.True);
    }

    [Test]
    public void Other_vendors_are_preserved_without_changing_defaults()
    {
        var result = new DataContract { Schema = [new SchemaObject { Name = "orders",
            CustomProperties = [new() { Vendor = "other", Property = "enforcePropertyOrder", Value = "invalid" }]
        }] }.ToCanonicalContract();
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.RequireValue().Assets[0].Schema!.EnforcePropertyOrder, Is.True);
        Assert.That(result.RequireValue().Assets[0].Extensions.Namespaces, Does.Contain("odcs"));
    }

    private static CustomProperty Declaration(string name, object? value) => new() { Vendor = "packata", Property = name, Value = value };
}
