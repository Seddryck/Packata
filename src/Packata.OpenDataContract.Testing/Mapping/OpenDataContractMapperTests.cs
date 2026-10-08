using NUnit.Framework;
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
    public void Map_PreservesFixedWidthExtensionMetadata()
    {
        var property = new SchemaProperty { Name = "id" };
        property.CustomProperties.Add(new CustomProperty
        {
            Vendor = "packata",
            Property = "fixedWidthOffset",
            Value = 0
        });
        property.CustomProperties.Add(new CustomProperty
        {
            Vendor = "packata",
            Property = "fixedWidthLength",
            Value = 8
        });
        var document = new DataContract
        {
            Id = "customers",
            Schema = [new SchemaObject { Name = "customers", Properties = [property] }],
            Servers =
            [
                new CustomServer
                {
                    Server = "customers-file",
                    Type = "custom",
                    Format = "fixed-width",
                    Encoding = "UTF-8"
                }
            ]
        };

        var result = new OpenDataContractMapper().Map(document).RequireValue();
        var customProperties = (Dictionary<string, object?>[])result.Assets[0].Schema!.Fields[0]
            .Extensions["odcs"]["customProperties"]!;

        Assert.Multiple(() =>
        {
            Assert.That(result.Endpoints[0].Format!.Name, Is.EqualTo("fixed-width"));
            Assert.That(result.Endpoints[0].Format!.Encoding, Is.EqualTo("UTF-8"));
            Assert.That(customProperties, Has.Length.EqualTo(2));
            Assert.That(customProperties[0]["vendor"], Is.EqualTo("packata"));
            Assert.That(customProperties[0]["property"], Is.EqualTo("fixedWidthOffset"));
        });
    }
}
