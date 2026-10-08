using NUnit.Framework;
using System.Reflection;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;

namespace Packata.OpenDataContract.Testing.Mapping;

public class OpenDataContractMapperTests
{
    [Test]
    public void ToCanonicalContract_ReturnsCanonicalContract()
    {
        var document = new DataContract { Id = "orders", Name = "Orders" };

        var contract = document.ToCanonicalContract().RequireValue();

        Assert.That(contract.Identity.Id, Is.EqualTo("orders"));
    }

    [Test]
    public void Map_LocalFileContract_ReturnsCanonicalAssetAndEndpoint()
    {
        var document = new DataContract
        {
            Id = "orders-contract",
            Name = "orders",
            Version = "1.0.0",
            Status = "active",
            Domain = "sales",
            Schema =
            [
                new SchemaObject
                {
                    Name = "orders",
                    PhysicalName = "orders.csv",
                    PhysicalType = "table",
                    Properties =
                    [
                        new SchemaProperty
                        {
                            Name = "id",
                            PhysicalName = "order_id",
                            PhysicalType = "bigint",
                            PrimaryKey = true,
                            Required = true
                        }
                    ]
                }
            ],
            Servers =
            [
                new LocalFilesServer
                {
                    Server = "production",
                    Type = "local",
                    Path = "data/orders.csv",
                    Format = "csv"
                }
            ]
        };

        var result = new OpenDataContractMapper().Map(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Value!.Assets, Has.Count.EqualTo(1));
            Assert.That(result.Value.Assets[0].Kind, Is.EqualTo(AssetKind.Table));
            Assert.That(result.Value.Assets[0].Schema!.PrimaryKey, Is.EqualTo(new[] { "id" }));
            Assert.That(result.Value.Assets[0].Schema!.Fields[0].PhysicalName, Is.EqualTo("order_id"));
            Assert.That(result.Value.Assets[0].EndpointBindings[0].EndpointId, Is.EqualTo("production"));
            Assert.That(
                ((PathLocation)result.Value.Endpoints[0].Location).Paths,
                Is.EqualTo(new[] { "data/orders.csv" }));
        });
    }

    [Test]
    public void Map_MultipleServers_DoesNotGuessSchemaBindings()
    {
        var document = new DataContract
        {
            Id = "orders",
            Version = "1.0.0",
            Status = "active",
            Schema = [new SchemaObject { Name = "orders" }],
            Servers =
            [
                new LocalFilesServer { Server = "a", Type = "local", Path = "a.csv", Format = "csv" },
                new LocalFilesServer { Server = "b", Type = "local", Path = "b.csv", Format = "csv" }
            ]
        };

        var result = new OpenDataContractMapper().Map(document);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Value!.Assets[0].EndpointBindings, Is.Empty);
            Assert.That(result.Diagnostics, Has.One.Property("Code").EqualTo("ODCS001"));
        });
    }

    [Test]
    public void Map_preserves_property_foreign_key_relationships()
    {
        var document = new DataContract
        {
            Id = "orders", Schema =
            [
                new SchemaObject
                {
                    Name = "orders", Properties =
                    [
                        new SchemaProperty
                        {
                            Name = "customer_id", Relationships =
                            [new Relationship { Id = "customer", To = "schema/customers/properties/id" }]
                        }
                    ]
                },
                new SchemaObject { Name = "customers", Properties = [new SchemaProperty { Name = "id" }] }
            ]
        };

        var relationship = new OpenDataContractMapper().Map(document).RequireValue()
            .Assets[0].Schema!.Relationships.Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.Fields, Is.EqualTo(new[] { "customer_id" }));
            Assert.That(relationship.TargetAsset, Is.EqualTo("customers"));
            Assert.That(relationship.TargetFields, Is.EqualTo(new[] { "id" }));
            Assert.That(relationship.Name, Is.EqualTo("customer"));
        });
    }

    [Test]
    public void Map_preserves_logical_type_range_and_length_constraints()
    {
        var amount = LogicalProperty("amount", "number", new()
        {
            ["minimum"] = "0.1", ["maximum"] = "100", ["exclusiveMaximum"] = "true"
        });
        var code = LogicalProperty("code", "string", new()
        {
            ["minLength"] = "2", ["maxLength"] = "8", ["pattern"] = "^[A-Z]+$"
        });
        code.Enum = [new EnumerationValue { Value = "AA" }, new EnumerationValue { Value = "BB" }];
        var document = new DataContract
        {
            Id = "orders", Schema = [new SchemaObject { Name = "orders", Properties = [amount, code] }]
        };

        var fields = new OpenDataContractMapper().Map(document).RequireValue().Assets[0].Schema!.Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields[0].Constraints.Select(value => value.Kind),
                Is.EqualTo(new[] { "minimum", "exclusiveMaximum" }));
            Assert.That(fields[1].Constraints.Select(value => value.Kind),
                Is.EqualTo(new[] { "minLength", "maxLength", "pattern", "enum" }));
        });
    }

    private static SchemaProperty LogicalProperty(string name, string type, Dictionary<string, object> options)
    {
        var property = new SchemaProperty { Name = name };
        typeof(SchemaBaseProperty).GetProperty("LogicalTypeDiscriminator",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(property, type);
        typeof(SchemaBaseProperty).GetProperty("LogicalTypeOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(property, options);
        return property;
    }
}
