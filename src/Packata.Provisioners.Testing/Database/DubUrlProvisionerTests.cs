using System.Data;
using DubUrl;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Querying.TypeMapping;
using DubUrl.Schema;
using DubUrl.Schema.Constraints;
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
    public void DeploySchema_maps_canonical_assets_and_generates_foreign_keys()
    {
        var fixture = CreateFixture();
        var customers = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table,
            new DataSchema(
                [new DataField("CustomerId", "integer", Required: true), new DataField("Name", "string")],
                ["CustomerId"], [new DataRelationship(["CustomerId"], "Other", ["Id"])]));
        var other = new DataAsset("Other", "Other", "Other", null, AssetKind.Table,
            new DataSchema([new DataField("Id", "integer")], ["Id"]));
        var contract = new DataContract(new("contract"), new(), [customers, other], [], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        fixture.Renderer.Verify(x => x.Render(It.IsAny<Schema>()), Times.Once);
        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object,
            It.Is<string>(script => script.Contains("FOREIGN KEY ([CustomerId])")
                && script.Contains("REFERENCES [Other] ([Id])"))), Times.Once);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void DeploySchema_generates_composite_primary_key_with_physical_names()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema(
            [new DataField("tenant", "string", PhysicalName: "tenant_id"),
             new DataField("order", "integer", PhysicalName: "order_id")],
            ["tenant", "order"]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        var primaryKey = rendered!.Tables["Customer"].Constraints.Get<PrimaryKeyConstraint>();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(primaryKey!.Columns.Keys, Is.EqualTo(new[] { "tenant_id", "order_id" }));
        });
    }

    [Test]
    public void DeploySchema_generates_pattern_checks_for_supported_dialect()
    {
        var fixture = CreateFixture("mysql://./mydb");
        var contract = Contract(new DataSchema(
            [new DataField("code", "string", Constraints: [new("pattern", "^[A-Z]+$")]) ]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object,
            It.Is<string>(script => script.Contains("CHECK (`code` REGEXP '^[A-Z]+$')"))), Times.Once);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void DeploySchema_generates_membership_checks()
    {
        var fixture = CreateFixture();
        var contract = Contract(new DataSchema(
            [new DataField("status", "string", Constraints: [new("enum", new[] { "new", "closed" })]) ]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object,
            It.Is<string>(script => script.Contains("CHECK ([status] IN ('new', 'closed'))"))), Times.Once);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void DeploySchema_honors_supported_physical_types()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema(
            [new DataField("code", "string", PhysicalType: "varchar(12)"),
             new DataField("amount", "number", PhysicalType: "decimal(10, 2)")]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(((VarLengthColumn)rendered!.Tables["Customer"].Columns["code"]).Length, Is.EqualTo(12));
            Assert.That(((NumericColumn)rendered.Tables["Customer"].Columns["amount"]).Precision, Is.EqualTo(10));
            Assert.That(((NumericColumn)rendered.Tables["Customer"].Columns["amount"]).Scale, Is.EqualTo(2));
        });
    }

    [Test]
    public void DeploySchema_generates_table_and_column_comments()
    {
        var fixture = CreateFixture("postgres://localhost/mydb");
        var asset = new DataAsset("customers", "Customers", "customers", "Customer master data",
            AssetKind.Table, new DataSchema([new DataField("name", "string", Description: "Display name")]));
        var contract = new DataContract(new("contract"), new(), [asset], [], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object,
            It.Is<string>(script => script.Contains("COMMENT ON TABLE \"customers\" IS 'Customer master data';")
                && script.Contains("COMMENT ON COLUMN \"customers\".\"name\" IS 'Display name';"))), Times.Once);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void DeploySchema_qualifies_tables_from_bound_endpoint_metadata()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var endpoint = new DataEndpoint("warehouse", null, EndpointKind.Database, null,
            new ConnectionLocation("mssql", Database: "catalog", Namespace: "sales"));
        var asset = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table,
            new DataSchema([new DataField("id", "integer")]), [new EndpointBinding(endpoint.Id)]);
        var contract = new DataContract(new("contract"), new(), [asset], [endpoint], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(rendered!.Tables.Keys, Is.EqualTo(new[] { "catalog.sales.Customer" }));
        });
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
        readerFactory.Setup(x => x.OpenAsync(
                new DataEndpointReadRequest(endpoint, asset.Schema, null), It.IsAny<CancellationToken>()))
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

    [Test]
    public async Task LoadDataAsync_passes_distinct_asset_paths_for_a_shared_endpoint()
    {
        var fixture = CreateFixture();
        var endpoint = new DataEndpoint("source", null, EndpointKind.Database, null,
            new ConnectionLocation("mssql", "server", Database: "sales"));
        var schema = new DataSchema([new DataField("Id", "integer")]);
        var first = new DataAsset("customers", "Customers", "Customers", null, AssetKind.Table,
            schema, [new EndpointBinding(endpoint.Id, "source_customers")]);
        var second = new DataAsset("orders", "Orders", "Orders", null, AssetKind.Table,
            schema, [new EndpointBinding(endpoint.Id, "source_orders")]);
        var contract = new DataContract(new("contract"), new(), [first, second], [endpoint], new());
        var firstReader = new Mock<IDataReader>();
        var secondReader = new Mock<IDataReader>();
        var readerFactory = new Mock<IDataEndpointReaderFactory>();
        readerFactory.Setup(x => x.OpenAsync(
                new DataEndpointReadRequest(endpoint, schema, "source_customers"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstReader.Object);
        readerFactory.Setup(x => x.OpenAsync(
                new DataEndpointReadRequest(endpoint, schema, "source_orders"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondReader.Object);

        var diagnostics = await fixture.WithReader(readerFactory.Object).LoadDataAsync(contract);

        Assert.That(diagnostics, Is.Empty);
        readerFactory.VerifyAll();
    }

    private static DataContract Contract(DataSchema schema)
    {
        var asset = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table, schema);
        return new DataContract(new("contract"), new(), [asset], [], new());
    }

    private static Fixture CreateFixture(string connectionUrl = "mssql://./mydb")
    {
        var typeMapper = new Mock<IDbTypeMapper>();
        var functionMapper = new Mock<ISqlFunctionMapper>();
        var dialect = new Mock<IDialect>();
        dialect.Setup(x => x.DbTypeMapper).Returns(typeMapper.Object);
        dialect.Setup(x => x.SqlFunctionMapper).Returns(functionMapper.Object);
        var connection = new Mock<ConnectionUrl>(connectionUrl);
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
