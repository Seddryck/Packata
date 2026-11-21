using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;

namespace Packata.OpenDataContract.Testing.Mapping;

public sealed class CustomPropertyBindingTests
{
    [Test]
    public async Task Overrides_replace_the_built_in_selector_and_are_isolated()
    {
        var schema = new SchemaObject
        {
            Name = "orders",
            CustomProperties = [Declaration("allowAdditionalProperties", true, "packata"), Declaration("additionalProperties", false),
                Declaration("additionalProperties", true, "another-vendor")],
        };
        var document = new DataContract { Schema = [schema] };
        var none = OdcsMappingOptions.NoCustomPropertyBindings;
        var options = none with { Schema = none.Schema with { AllowAdditionalProperties = OdcsBindingOverride.Unvendored("additionalProperties") } };
        var results = await Task.WhenAll(Task.Run(() => document.ToCanonicalContract(OdcsMappingOptions.Default)),
            Task.Run(() => document.ToCanonicalContract(options)));
        Assert.That(results[0].RequireValue().Assets[0].Schema!.AllowAdditionalProperties, Is.True);
        var asset = results[1].RequireValue().Assets[0];
        Assert.That(asset.Schema!.AllowAdditionalProperties, Is.False);
        var metadata = (Dictionary<string, object?>[])asset.Extensions["odcs"]["customProperties"]!;
        Assert.That(metadata.Select(entry => (entry["property"], entry["vendor"], entry["value"])),
            Is.EqualTo(schema.CustomProperties.Select(entry => ((object?)entry.Property, (object?)entry.Vendor, entry.Value))));
    }

    [Test]
    public void Supports_inherited_named_and_explicitly_unvendored_selectors()
    {
        var document = new DataContract { Schema = [new() { Name = "orders", CustomProperties =
            [Declaration("ordered", false, "packata"), Declaration("ordered", "false"),
                Declaration("enforcePropertyOrder", false, "acme"), Declaration("enforcePropertyOrder", false)] }] };
        bool EnforcesOrder(OdcsBindingOverride selector) => document.ToCanonicalContract(
            new OdcsMappingOptions { Schema = new() { EnforcePropertyOrder = selector } })
            .RequireValue().Assets[0].Schema!.EnforcePropertyOrder;
        Assert.That(EnforcesOrder(OdcsBindingOverride.UseBuiltInVendor("ordered")), Is.False);
        Assert.That(EnforcesOrder(OdcsBindingOverride.ForVendor("acme")), Is.False);
        Assert.That(EnforcesOrder(OdcsBindingOverride.Unvendored()), Is.False);
        Assert.That(EnforcesOrder(OdcsBindingOverride.ForVendor("missing")), Is.True);
    }

    [Test]
    public void Configured_booleans_keep_strict_parsing_and_placement()
    {
        var document = new DataContract { Schema = [new() { Name = "orders", CustomProperties =
            [Declaration("additionalProperties", "false"), Declaration("additionalProperties", false)],
            Properties = [new() { Name = "id", CustomProperties = [Declaration("additionalProperties", true),
                Declaration("allowAdditionalProperties", true, "packata")] }] }] };
        var options = new OdcsMappingOptions { Schema = new() { AllowAdditionalProperties = OdcsBindingOverride.Unvendored("additionalProperties") } };
        var diagnostics = document.ToCanonicalContract(options).Diagnostics;
        Assert.That(diagnostics.Select(item => item.SourcePath), Is.EquivalentTo(new[]
        {
            "$/schema/0/properties/0/customProperties/0", "schema/orders/customProperties/0", "schema/orders/customProperties/1",
        }));
        Assert.That(diagnostics.All(item => item.Code == "ODCS008" && item.Message.Contains("'additionalProperties'")), Is.True);
    }

    [Test]
    public void Rejects_collisions_with_builtin_selectors_using_the_owners_comparison()
    {
        Assert.That(() => new OpenDataContractMapper(new() { Server = new()
        {
            Delimiter = OdcsBindingOverride.ForVendor("PACKATA", "QUOTECHAR"),
        } }), Throws.ArgumentException.With.Message.Contains("server.quoteChar"));
        Assert.That(() => new OpenDataContractMapper(new() { Field = new()
        {
            DefaultValue = OdcsBindingOverride.Unvendored(" "),
        } }), Throws.ArgumentException.With.Message.Contains("field.defaultValue"));
    }

    [TestCase("csv", ";")]
    [TestCase("fixed-width", null)]
    public void Server_override_preserves_format_gate_and_native_precedence(string format, string? expected)
    {
        var server = new CustomServer { Server = "orders", Type = "custom", Format = format, Delimiter = ",",
            CustomProperties = [Declaration("delimiter", "|", "packata"), Declaration("SEPARATOR", ":", "ACME"),
                Declaration("separator", ";", "acme")] };
        var document = new DataContract { Servers = [server] };
        var options = new OdcsMappingOptions { Server = new() { Delimiter = OdcsBindingOverride.ForVendor("acme", "separator") } };
        var endpoint = document.ToCanonicalContract(options).RequireValue().Endpoints.Single();
        Assert.That(endpoint.Format!.Options.TryGetValue("delimiter", out var delimiter), Is.EqualTo(expected is not null));
        Assert.That(delimiter, Is.EqualTo(expected));
        if (expected is null) return;
        server.CustomProperties.RemoveRange(1, 2);
        endpoint = document.ToCanonicalContract(options).RequireValue().Endpoints.Single();
        Assert.That(endpoint.Format!.Options["delimiter"], Is.EqualTo(","));
    }

    [Test]
    public void Renamed_category_and_default_bindings_reuse_existing_payloads_and_conflicts()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            schema:
              - name: lookup
                physicalType: table
                customProperties:
                  - {property: lookupColumns, value: {value: key}}
                properties:
                  - {name: key, logicalType: string, required: true, unique: true}
              - name: orders
                properties:
                  - name: code
                    logicalType: string
                    customProperties:
                      - {property: lookupValues, value: {resource: lookup}}
                      - {property: initial, value: {type: string, value: a}}
            """));
        var document = new DataContractFactory().LoadFromStream(stream);
        var field = document.Schema[1].Properties[0];
        var options = new OdcsMappingOptions
        {
            Schema = new() { CategoryFieldMap = OdcsBindingOverride.Unvendored("lookupColumns") },
            Field = new() { Categories = OdcsBindingOverride.Unvendored("lookupValues"),
                DefaultValue = OdcsBindingOverride.Unvendored("initial") },
        };
        var contract = document.ToCanonicalContract(options).RequireValue();
        Assert.That(contract.Assets[0].CategoryTable, Is.Not.Null);
        Assert.That(contract.Assets[1].Schema!.Fields[0].ValueDomain, Is.Not.Null);
        Assert.That(contract.Assets[1].Schema!.Fields[0].DefaultValue!.Value, Is.EqualTo("a"));
        field.CustomProperties.Add(Declaration("defaultExpression", new Dictionary<string, object?> { ["kind"] = "current-user" }, "packata"));
        var diagnostic = document.ToCanonicalContract(options).Diagnostics.Single();
        Assert.That(diagnostic.Code, Is.EqualTo("DEFAULT001"));
        Assert.That(diagnostic.Message, Does.Contain("'initial'").And.Contain("'defaultExpression'"));
    }

    [Test]
    public void Presets_control_schema_field_and_server_bindings_without_losing_native_mapping()
    {
        var field = new SchemaProperty { Name = "id", Required = true, PrimaryKey = true,
            CustomProperties = [Declaration("defaultExpression", new Dictionary<string, object?> { ["kind"] = "current-user" }, "packata")] };
        var schema = new SchemaObject { Name = "orders", Properties = [field], CustomProperties =
            [Declaration("allowAdditionalProperties", true, "packata"), Declaration("enforcePropertyOrder", false, "packata"),
                Declaration("additionalProperties", "false", "other"), Declaration("additionalProperties", true)] };
        var server = new CustomServer { Server = "orders", Type = "custom", Format = "csv", Delimiter = ",",
            CustomProperties = [Declaration("delimiter", "|", "packata")] };
        var document = new DataContract { Schema = [schema], Servers = [server] };

        var defaults = OdcsMappingOptions.Default;
        foreach (var mapped in new[] { document.ToCanonicalContract(), document.ToCanonicalContract(defaults) })
        {
            var contract = mapped.RequireValue();
            Assert.That(contract.Assets[0].Schema!.AllowAdditionalProperties, Is.True);
            Assert.That(contract.Assets[0].Schema!.EnforcePropertyOrder, Is.False);
            Assert.That(contract.Assets[0].Schema!.Fields[0].DefaultExpression!.Kind, Is.EqualTo(ColumnDefaultKind.CurrentUser));
            Assert.That(contract.Endpoints[0].Format!.Options["delimiter"], Is.EqualTo("|"));
        }

        var none = OdcsMappingOptions.NoCustomPropertyBindings;
        var disabled = document.ToCanonicalContract(none).RequireValue();
        var asset = disabled.Assets[0];
        Assert.That(asset.Schema!.AllowAdditionalProperties, Is.False);
        Assert.That(asset.Schema.EnforcePropertyOrder, Is.True);
        Assert.That(asset.Schema.Fields[0].DefaultExpression, Is.Null);
        Assert.That(asset.Schema.Fields[0].Required, Is.True);
        Assert.That(asset.Schema.PrimaryKey, Is.EqualTo(new[] { "id" }));
        Assert.That(asset.Schema.Fields[0].Extensions["odcs"]["customProperties"], Is.Not.Empty);
        Assert.That(disabled.Endpoints[0].Format!.Options["delimiter"], Is.EqualTo(","));

        var onlySelected = none with
        {
            Schema = none.Schema with { AllowAdditionalProperties = OdcsBindingOverride.Unvendored("additionalProperties") },
            Field = none.Field with { DefaultExpression = OdcsBindingOverride.BuiltIn },
        };
        var selected = document.ToCanonicalContract(onlySelected).RequireValue().Assets[0].Schema!;
        Assert.That(selected.AllowAdditionalProperties, Is.True);
        Assert.That(selected.EnforcePropertyOrder, Is.True);
        Assert.That(selected.Fields[0].DefaultExpression, Is.Not.Null);

        var withoutExpression = defaults with { Field = defaults.Field with { DefaultExpression = OdcsBindingOverride.Disabled } };
        Assert.That(document.ToCanonicalContract(withoutExpression).RequireValue().Assets[0].Schema!.Fields[0].DefaultExpression, Is.Null);
        Assert.That(document.ToCanonicalContract(defaults).RequireValue().Assets[0].Schema!.Fields[0].DefaultExpression, Is.Not.Null);
        Assert.That(document.ToCanonicalContract(none).RequireValue().Assets[0].Schema!.AllowAdditionalProperties, Is.False);
    }

    [Test]
    public void Disabled_selectors_do_not_validate_payloads_check_placement_or_collide()
    {
        var schema = new SchemaObject { Name = "orders", CustomProperties =
            [Declaration("enforcePropertyOrder", true, "packata"), Declaration("categoryFieldMap", "invalid", "packata")],
            Properties = [new() { Name = "id", CustomProperties = [Declaration("enforcePropertyOrder", true, "packata")] }] };
        var document = new DataContract { Schema = [schema] };
        Assert.That(document.ToCanonicalContract().Diagnostics.Select(item => item.Code), Is.EquivalentTo(new[] { "ODCS008", "ODCS007" }));
        Assert.That(document.ToCanonicalContract(OdcsMappingOptions.NoCustomPropertyBindings).Diagnostics, Is.Empty);

        var defaults = OdcsMappingOptions.Default;
        var options = defaults with { Schema = defaults.Schema with
        {
            AllowAdditionalProperties = OdcsBindingOverride.UseBuiltInVendor("enforcePropertyOrder"),
            EnforcePropertyOrder = OdcsBindingOverride.Disabled,
            CategoryFieldMap = OdcsBindingOverride.Disabled,
        } };
        schema.Properties[0].CustomProperties.Clear();
        var mapped = document.ToCanonicalContract(options).RequireValue();
        Assert.That(mapped.Assets[0].Schema!.AllowAdditionalProperties, Is.True);
    }

    private static CustomProperty Declaration(string name, object? value, string? vendor = null)
        => new() { Property = name, Value = value, Vendor = vendor };
}
