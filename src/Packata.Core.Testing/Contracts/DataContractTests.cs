using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class DataContractTests
{
    [Test]
    public void Construct_WithNestedSchema_PreservesContractStructure()
    {
        var child = new DataField("city", "string", Required: true);
        var field = new DataField("address", "object", Children: [child]);
        var asset = new DataAsset(
            "customers",
            "customers",
            "dbo.customers",
            null,
            AssetKind.Table,
            new DataSchema([field]),
            [new EndpointBinding("production")]);
        var endpoint = new DataEndpoint(
            "production",
            "Production",
            EndpointKind.Database,
            "prod",
            new ConnectionLocation("postgresql", "localhost", 5432, "crm", "dbo"));

        var contract = new DataContract(
            new ContractIdentity("customer-contract", "customers", "1.0.0", "active"),
            new ContractMetadata(Domain: "sales", Tags: ["customer"]),
            [asset],
            [endpoint],
            new ContractGovernance());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Assets[0].Schema!.Fields[0].Children[0].Name, Is.EqualTo("city"));
            Assert.That(contract.Assets[0].EndpointBindings[0].EndpointId, Is.EqualTo("production"));
            Assert.That(contract.Endpoints[0].Location, Is.TypeOf<ConnectionLocation>());
            Assert.That(contract.Metadata.Tags, Does.Contain("customer"));
        });
    }

    [Test]
    public void OptionalCollections_DefaultToEmptyCollections()
    {
        var contract = new DataContract(
            new ContractIdentity("empty"),
            new ContractMetadata(),
            [],
            [],
            new ContractGovernance());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Metadata.Tags, Is.Empty);
            Assert.That(contract.Governance.ServiceLevels, Is.Empty);
            Assert.That(contract.Governance.References, Is.Empty);
        });
    }
}
