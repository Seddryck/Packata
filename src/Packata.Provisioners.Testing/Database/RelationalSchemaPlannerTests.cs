using DubUrl;
using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;
namespace Packata.Provisioners.Testing.Database;
public class RelationalSchemaPlannerTests
{
    [Test]
    public void Offline_and_provisioning_plans_have_identical_mapping_sql_and_diagnostics()
    {
        var dialect = OfflineSqlDialects.Resolve("sqlserver");
        var connection = new Mock<ConnectionUrl>("mssql://unused/db");
        connection.Setup(c => c.Dialect).Returns(dialect);
        var renderer = new SchemaScriptRenderer(dialect);
        var contract = new DataContract(new("example"), new(), [
            new("orders", "orders", "Order Detail", null, AssetKind.Table,
                new([new("id", "integer", PhysicalName: "Order Id", Required: true), new("label", "string", Constraints: [new("pattern", "x")])], ["id"]),
                [new("db")])],
            [new("db", "database", EndpointKind.Database, null, new ConnectionLocation("mssql", "unused", Database: "example", Namespace: "sales"))], new());
        var offline = new RelationalSchemaPlanner(dialect, renderer).PlanSchema(contract);
        var provisioner = new DubUrlProvisioner(connection.Object);
        var connected = provisioner.PlanSchema(contract);
        Assert.That(offline.Script, Is.EqualTo(connected.Script));
        Assert.That(offline.Diagnostics, Is.EqualTo(connected.Diagnostics));
        Assert.That(offline.Script, Does.Contain("[Order Detail]").And.Contain("[Order Id]"));
        connection.Verify(c => c.Connect(), Times.Never);
    }
    [Test]
    public void Foreign_keys_outside_selection_are_diagnosed_and_never_rendered()
    {
        var contract = new DataContract(new("example"), new(), [
            new("orders", "orders", null, null, AssetKind.Table, new([new("id", "integer")], Relationships: [new(["id"], "customers", ["id"])])),
            new("customers", "customers", null, null, AssetKind.Table, new([new("id", "integer")], ["id"]))], [], new());
        var planner = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("postgresql"));
        var all = planner.PlanSchema(contract);
        Assert.That(all.Script, Does.Contain("FOREIGN KEY"));
        Assert.That(all.Diagnostics, Is.Empty);
        var selected = planner.PlanSchema(contract, assetId: "orders");
        Assert.That(selected.Diagnostics.Select(d => d.Code), Does.Contain("PROV003"));
        Assert.That(selected.Script, Does.Not.Contain("REFERENCES"));
    }
}
