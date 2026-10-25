using System.Data;
using System.Data.Common;
using DubUrl;
using DubUrl.Mapping;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Querying.TypeMapping;
using DubUrl.Schema;
using DubUrl.Schema.Constraints;
using Moq;
using DubUrl.Provider.SqlServer.Sql;
using DubUrl.Provider.DuckDb.Sql;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Core.Reading;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class DubUrlProvisionerTests
{
    [Test]
    public void PlanSchema_renders_schema_and_preserves_diagnostics_without_deploying()
    {
        var fixture = CreateFixture();
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>())).Returns("PLANNED SCRIPT");
        var contract = Contract(new DataSchema([new DataField("id", "integer")], ["missing"]));

        var plan = fixture.Provisioner.PlanSchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Script, Is.EqualTo("PLANNED SCRIPT"));
            Assert.That(plan.Diagnostics.Single().Code, Is.EqualTo("PROV002"));
        });
        fixture.Deployer.Verify(
            x => x.DeploySchema(It.IsAny<ConnectionUrl>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void DeploySchema_with_plan_executes_exact_script_without_rerendering()
    {
        var fixture = CreateFixture();
        var plan = new SchemaProvisioningPlan("REVIEWED SCRIPT",
            [new("PROV002", "customers", "Diagnostic")]);

        fixture.Provisioner.DeploySchema(plan);

        fixture.Deployer.Verify(
            x => x.DeploySchema(fixture.Connection.Object, "REVIEWED SCRIPT"), Times.Once);
        fixture.Renderer.Verify(x => x.Render(It.IsAny<Schema>()), Times.Never);
    }

    [Test]
    public void DeploySchema_maps_canonical_assets_and_generates_foreign_keys()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var customers = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table,
            new DataSchema(
                [new DataField("CustomerId", "integer", Required: true), new DataField("Name", "string")],
                ["CustomerId"], [new DataRelationship(["CustomerId"], "Other", ["Id"])]));
        var other = new DataAsset("Other", "Other", "Other", null, AssetKind.Table,
            new DataSchema([new DataField("Id", "integer")], ["Id"]));
        var contract = new DataContract(new("contract"), new(), [customers, other], [], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        var foreignKey = rendered!.Tables["Customer"].Constraints.OfType<DubUrl.Schema.Constraints.ForeignKeyConstraint>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(foreignKey.SourceColumns, Is.EqualTo(new[] { "CustomerId" }));
            Assert.That(foreignKey.TargetTable.Name, Is.EqualTo("Other"));
            Assert.That(foreignKey.TargetColumns, Is.EqualTo(new[] { "Id" }));
        });
        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object, "CREATE TABLE"), Times.Once);
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
            Assert.That(rendered.Tables["Customer"].Columns.Values
                .All(column => column.Constraints.Get<NotNullableConstraint>() is not null), Is.True);
        });
    }

    [Test]
    public void DeploySchema_reports_unresolved_primary_key_without_rendering_it()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema([new DataField("id", "integer")], ["missing"]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV002"));
            Assert.That(rendered!.Tables["Customer"].Constraints.OfType<PrimaryKeyConstraint>(), Is.Empty);
        });
    }

    [Test]
    public void DeploySchema_reports_oversized_physical_type_without_throwing()
    {
        var fixture = CreateFixture();
        var contract = Contract(new DataSchema(
            [new DataField("code", "string", PhysicalType: "varchar(999999999999999999999)")]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV005"));
    }

    [Test]
    public void DeploySchema_skips_tables_with_duplicate_physical_columns()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema(
            [new DataField("first", "string", PhysicalName: "value"),
             new DataField("second", "string", PhysicalName: "value")]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV008"));
            Assert.That(rendered!.Tables, Is.Empty);
        });
    }

    [Test]
    public void DeploySchema_generates_pattern_checks_for_supported_dialect()
    {
        var fixture = CreateFixture("mysql://./mydb");
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema(
            [new DataField("code", "string", Constraints: [new("pattern", "^[A-Z]+$")])]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        var regex = rendered!.Tables["Customer"].Columns["code"].Constraints
            .OfType<RegexCheckConstraint>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(regex.Pattern, Is.EqualTo("^[A-Z]+$"));
        });
    }

    [Test]
    public void DeploySchema_generates_membership_checks()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var contract = Contract(new DataSchema(
            [new DataField("status", "string", Constraints: [new("enum", new[] { "new", "closed" })])]));

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        var membership = rendered!.Tables["Customer"].Columns["status"].Constraints
            .OfType<MembershipCheckConstraint>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(membership.Values, Is.EqualTo(new[] { "new", "closed" }));
        });
    }

    [Test]
    public void DeploySchema_uses_domain_values_instead_of_labels_and_does_not_expand_unresolved_references()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var a = ScalarValue.From("new");
        var b = ScalarValue.From("closed");
        var contract = Contract(new DataSchema(
        [
            new DataField("status","string",ValueDomain:new([new(a,"New item","Description"),new(b,"Closed item")],[a])),
            new DataField("unknown","string",ValueDomain:new(reference:new("codes")))
        ]));
        var diagnostics = fixture.Provisioner.DeploySchema(contract);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(rendered!.Tables["Customer"].Columns["status"].Constraints.OfType<MembershipCheckConstraint>().Single().Values,
            Is.EqualTo(new[] { "new" }));
        Assert.That(rendered.Tables["Customer"].Columns["unknown"].Constraints.OfType<MembershipCheckConstraint>(), Is.Empty);
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
            Assert.That(rendered.Tables["Customer"].Columns["code"].NativeType!.Name, Is.EqualTo("varchar"));
            Assert.That(((NumericColumn)rendered.Tables["Customer"].Columns["amount"]).Precision, Is.EqualTo(10));
            Assert.That(((NumericColumn)rendered.Tables["Customer"].Columns["amount"]).Scale, Is.EqualTo(2));
        });
    }

    [Test]
    public void DeploySchema_generates_table_and_column_comments()
    {
        var fixture = CreateFixture("postgres://localhost/mydb");
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var asset = new DataAsset("customers", "Customers", "customers", "Customer master data",
            AssetKind.Table, new DataSchema([new DataField("name", "string", Description: "Display name")]));
        var contract = new DataContract(new("contract"), new(), [asset], [], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(rendered!.Tables["customers"].Description, Is.EqualTo("Customer master data"));
            Assert.That(rendered.Tables["customers"].Columns["name"].Description, Is.EqualTo("Display name"));
        });
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
            var identity = rendered!.Tables.Values.Single().Identity;
            Assert.That(identity.Database, Is.EqualTo("catalog"));
            Assert.That(identity.Schema, Is.EqualTo("sales"));
            Assert.That(identity.Name, Is.EqualTo("Customer"));
        });
    }

    [Test]
    public void DeploySchema_reports_ambiguous_endpoint_bindings_and_leaves_table_unqualified()
    {
        var fixture = CreateFixture();
        Schema? rendered = null;
        fixture.Renderer.Setup(x => x.Render(It.IsAny<Schema>()))
            .Callback<Schema>(value => rendered = value).Returns("CREATE TABLE");
        var first = new DataEndpoint("first", null, EndpointKind.Database, null,
            new ConnectionLocation("mssql", Database: "one"));
        var second = new DataEndpoint("second", null, EndpointKind.Database, null,
            new ConnectionLocation("mssql", Database: "two"));
        var asset = new DataAsset("customers", "Customer", "Customer", null, AssetKind.Table,
            new DataSchema([new DataField("id", "integer")]),
            [new EndpointBinding(first.Id), new EndpointBinding(second.Id)]);
        var contract = new DataContract(new("contract"), new(), [asset], [first, second], new());

        var diagnostics = fixture.Provisioner.DeploySchema(contract);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV007"));
            Assert.That(rendered!.Tables.Values.Single().Identity, Is.EqualTo(new DatabaseObjectName("Customer")));
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

    [Test]
    public async Task ExecuteAsync_rejects_bulk_loading_before_DDL_or_source_reads()
    {
        var fixture = CreateFixture();
        var readers = new Mock<IDataEndpointReaderFactory>(MockBehavior.Strict);
        var provisioner = fixture.WithReader(readers.Object, new FixedCapabilities(new(true, true, false)));
        var error = await Assert.ThrowsAsync<DatabaseOperationNotSupportedException>(async () =>
            await provisioner.ExecuteAsync(Contract(new DataSchema([new DataField("Id", "integer")]))));
        Assert.That(error!.Operation, Is.EqualTo(DatabaseOperation.BulkLoading));
        Assert.That(error.Message, Does.Contain("BulkLoading").And.Contain("Provider"));
        fixture.Deployer.Verify(x => x.DeploySchema(It.IsAny<ConnectionUrl>(), It.IsAny<string>()), Times.Never);
        fixture.BulkFactory.Verify(x => x.Create(It.IsAny<ConnectionUrl>()), Times.Never);
        readers.VerifyNoOtherCalls();
    }

    [Test]
    public void Schema_only_remains_available_without_bulk_loading()
    {
        var fixture = CreateFixture();
        var provisioner = fixture.WithReader(Mock.Of<IDataEndpointReaderFactory>(), new FixedCapabilities(new(true, true, false)));
        provisioner.DeploySchema(Contract(new DataSchema([new DataField("Id", "integer")])));
        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object, "CREATE TABLE"), Times.Once);
        Assert.That(provisioner.GetOperationSupport(), Is.EqualTo(new DatabaseOperationSupport(true, true, false)));
    }

    [Test]
    public async Task ExecuteAsync_rejects_schema_deployment_before_DDL_or_source_reads()
    {
        var fixture = CreateFixture();
        var readers = new Mock<IDataEndpointReaderFactory>(MockBehavior.Strict);
        var provisioner = fixture.WithReader(readers.Object, new FixedCapabilities(new(true, false, true)));
        var error = await Assert.ThrowsAsync<DatabaseOperationNotSupportedException>(async () =>
            await provisioner.ExecuteAsync(Contract(new DataSchema([new DataField("Id", "integer")]))));
        Assert.That(error!.Operation, Is.EqualTo(DatabaseOperation.SchemaDeployment));
        fixture.Deployer.Verify(x => x.DeploySchema(It.IsAny<ConnectionUrl>(), It.IsAny<string>()), Times.Never);
        readers.VerifyNoOtherCalls();
    }

    [Test]
    public void ExecuteAsync_checks_cancellation_and_missing_reader_factory_before_DDL()
    {
        var fixture = CreateFixture();
        var contract = Contract(new DataSchema([new DataField("Id", "integer")]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await fixture.Provisioner.ExecuteAsync(contract, cancellationToken: cancellation.Token),
            Throws.TypeOf<OperationCanceledException>());
        Assert.That(async () => await fixture.Provisioner.ExecuteAsync(contract),
            Throws.InvalidOperationException.With.Message.Contains("reader factory"));
        fixture.Deployer.Verify(x => x.DeploySchema(It.IsAny<ConnectionUrl>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Default_capabilities_do_not_treat_an_arbitrary_dialect_as_provisioning_support()
    {
        var fixture = CreateFixture();
        Assert.That(new DubUrlOperationCapabilities().GetSupport(fixture.Connection.Object),
            Is.EqualTo(new DatabaseOperationSupport(true, false, false)));
    }

    [Test]
    public void Default_capabilities_use_the_registered_bulk_adapter_without_opening_connections()
    {
        var dialects = new DialectRegistryBuilder();
        dialects.AddDialect<TSqlDialect>(["mssql"]);
        var connection = new Mock<ConnectionUrl>("mssql://./test", new ProviderCatalogBuilder().Build());
        connection.Setup(x => x.Dialect).Returns(dialects.Build().Get<TSqlDialect>());
        connection.Setup(x => x.BulkCopyProvider).Returns(Mock.Of<IBulkCopyProvider>());
        var policy = new DubUrlOperationCapabilities();
        Assert.That(policy.GetSupport(connection.Object), Is.EqualTo(new DatabaseOperationSupport(true, true, true)));
        connection.Verify(x => x.Open(), Times.Never);
        connection.Setup(x => x.BulkCopyProvider).Returns((IBulkCopyProvider?)null);
        Assert.That(policy.GetSupport(connection.Object), Is.EqualTo(new DatabaseOperationSupport(true, true, false)));
        connection.Verify(x => x.Connect(), Times.Never);
    }

    [Test]
    public void A_DuckDB_dialect_without_a_bulk_adapter_does_not_imply_loading_support()
    {
        var dialects = new DialectRegistryBuilder();
        dialects.AddDialect<DuckDbDialect>(["duckdb"]);
        var connection = new Mock<ConnectionUrl>("duckdb://memory", new ProviderCatalogBuilder().Build());
        connection.Setup(x => x.Dialect).Returns(dialects.Build().Get<DuckDbDialect>());
        Assert.That(new DubUrlOperationCapabilities().GetSupport(connection.Object),
            Is.EqualTo(new DatabaseOperationSupport(true, true, false)));
    }

    [Test]
    public async Task ExecuteAsync_with_supported_operations_deploys_and_loads()
    {
        var fixture = CreateFixture();
        var diagnostics = await fixture.WithReader(Mock.Of<IDataEndpointReaderFactory>()).ExecuteAsync(
            Contract(new DataSchema([new DataField("Id", "integer")])));
        fixture.Deployer.Verify(x => x.DeploySchema(fixture.Connection.Object, "CREATE TABLE"), Times.Once);
        fixture.BulkFactory.Verify(x => x.Create(fixture.Connection.Object), Times.Once);
        Assert.That(diagnostics.Single().Code, Is.EqualTo("PROV001"));
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
        var scheme = connectionUrl.Split(':', 2)[0];
        dialect.Setup(x => x.Capabilities).Returns(new DialectCapabilities
        {
            ObjectNames = ObjectNameParts.Name | ObjectNameParts.Schema | ObjectNameParts.Database | ObjectNameParts.Catalog,
            SupportsRegularExpressionChecks = scheme is "mysql" or "postgres",
            SupportsComments = scheme is "postgres",
            NativeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "varchar", "decimal" }
        });
        var schemaRenderer = new Mock<ISchemaSqlRenderer>();
        schemaRenderer.Setup(x => x.RenderObjectName(dialect.Object,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns((IDialect _, string? catalog, string? database, string? schema, string name) =>
                string.Join('.', new[] { catalog, database, schema, name }.Where(value => value is not null)));
        dialect.Setup(x => x.SchemaSqlRenderer).Returns(schemaRenderer.Object);
        var connection = new Mock<ConnectionUrl>(connectionUrl, new ProviderCatalogBuilder().Build());
        connection.Setup(x => x.Dialect).Returns(dialect.Object);
        var renderer = new Mock<SchemaScriptRenderer>(dialect.Object, SchemaCreationOptions.None, null);
        renderer.Setup(x => x.Render(It.IsAny<Schema>())).Returns("CREATE TABLE");
        var deployer = new Mock<SchemaScriptDeployer>();
        var bulkCopy = new Mock<IBulkCopyEngine>();
        var bulkFactory = new Mock<BulkCopyEngineFactory>();
        bulkFactory.Setup(x => x.Create(connection.Object)).Returns(bulkCopy.Object);
        var provisioner = new DubUrlProvisioner(connection.Object, renderer.Object, deployer.Object,
            bulkCopyEngineFactory: bulkFactory.Object, capabilities: new FixedCapabilities(new(true, true, true)));
        return new(provisioner, connection, renderer, deployer, bulkFactory, bulkCopy);
    }

    private sealed record Fixture(DubUrlProvisioner Provisioner, Mock<ConnectionUrl> Connection,
        Mock<SchemaScriptRenderer> Renderer, Mock<SchemaScriptDeployer> Deployer,
        Mock<BulkCopyEngineFactory> BulkFactory, Mock<IBulkCopyEngine> BulkCopy)
    {
        public DubUrlProvisioner WithReader(IDataEndpointReaderFactory reader, IDatabaseOperationCapabilities? capabilities = null) =>
            new(Connection.Object, Renderer.Object, Deployer.Object,
                bulkCopyEngineFactory: BulkFactory.Object, readerFactory: reader,
                capabilities: capabilities ?? new FixedCapabilities(new(true, true, true)));
    }

    private sealed class FixedCapabilities(DatabaseOperationSupport support) : IDatabaseOperationCapabilities
    {
        public DatabaseOperationSupport GetSupport(ConnectionUrl connection) => support;
    }
}
