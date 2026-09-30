using System.Data;
using DubUrl;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Querying.TypeMapping;
using DubUrl.Schema;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Core.Reading;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class DubUrlProvisionerTests
{
    [Test]
    public void DeploySchema_maps_canonical_assets_and_reports_unsupported_relationships()
    {
        var fixture = CreateFixture();
        var contract = Contract(new DataSchema(
            [new DataField("CustomerId", "integer", Required: true), new DataField("Name", "string")],
            ["CustomerId"],
            [new DataRelationship(["CustomerId"], "Other", ["Id"])]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        fixture.Renderer.Verify(x => x.Render(It.IsAny<Schema>()), Times.Once);
        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object, "CREATE TABLE"), Times.Once);
        Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV003"));
    }

    [Test]
    public async Task LoadDataAsync_resolves_binding_through_canonical_reader_contract()
    {
        var fixture = CreateFixture();
        var endpoint = new DataEndpoint("customers-source", null, EndpointKind.File, null,
            new PathLocation(["customers.csv"]), new DataFormat("csv"));
        var asset = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table,
            new DataSchema([new DataField("CustomerId", "integer")]), [new EndpointBinding(endpoint.Id)]);
        var contract = new DataContract(new("contract"), new(), [asset], [endpoint], new());
        var reader = new Mock<IDataReader>();
        var readerFactory = new Mock<IDataEndpointReaderFactory>();
        readerFactory.Setup(x => x.OpenAsync(endpoint, asset.Schema, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reader.Object);
        var provisioner = fixture.WithReader(readerFactory.Object);

        var diagnostics = await provisioner.LoadDataAsync(contract);

        Assert.That(diagnostics, Is.Empty);
        fixture.BulkCopy.Verify(x => x.Write("Customer", reader.Object), Times.Once);
        readerFactory.VerifyAll();
    }

    [Test]
    public async Task LoadDataAsync_reports_missing_endpoint_binding()
    {
        var fixture = CreateFixture();
        var readerFactory = new Mock<IDataEndpointReaderFactory>();
        var diagnostics = await fixture.WithReader(readerFactory.Object).LoadDataAsync(Contract(
            new DataSchema([new DataField("Id", "integer")])));
        Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV001"));
    }

    private static DataContract Contract(DataSchema schema)
    {
        var asset = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table, schema);
        return new DataContract(new("contract"), new(), [asset], [], new());
    }

    private static Fixture CreateFixture()
    {
        var typeMapper = new Mock<IDbTypeMapper>();
        var functionMapper = new Mock<ISqlFunctionMapper>();
        var dialect = new Mock<IDialect>();
        dialect.Setup(x => x.DbTypeMapper).Returns(typeMapper.Object);
        dialect.Setup(x => x.SqlFunctionMapper).Returns(functionMapper.Object);
        var connection = new Mock<ConnectionUrl>("mssql://./mydb");
        connection.Setup(x => x.Dialect).Returns(dialect.Object);
        var renderer = new Mock<SchemaScriptRenderer>(dialect.Object, SchemaCreationOptions.None);
        renderer.Setup(x => x.Render(It.IsAny<Schema>())).Returns("CREATE TABLE");
        var deployer = new Mock<SchemaScriptDeployer>();
        var bulkCopy = new Mock<IBulkCopyEngine>();
        var bulkFactory = new Mock<BulkCopyEngineFactory>();
        bulkFactory.Setup(x => x.Create(connection.Object)).Returns(bulkCopy.Object);
        var provisioner = new DubUrlProvisioner(connection.Object, renderer.Object, deployer.Object,
            bulkCopyEngineFactory: bulkFactory.Object);
        return new(provisioner, connection, renderer, deployer, bulkFactory, bulkCopy);
    }

    private sealed record Fixture(DubUrlProvisioner Provisioner, Mock<ConnectionUrl> Connection,
        Mock<SchemaScriptRenderer> Renderer, Mock<SchemaScriptDeployer> Deployer,
        Mock<BulkCopyEngineFactory> BulkFactory, Mock<IBulkCopyEngine> BulkCopy)
    {
        public DubUrlProvisioner WithReader(IDataEndpointReaderFactory reader) =>
            new(Connection.Object, Renderer.Object, Deployer.Object,
                bulkCopyEngineFactory: BulkFactory.Object, readerFactory: reader);
    }
}
