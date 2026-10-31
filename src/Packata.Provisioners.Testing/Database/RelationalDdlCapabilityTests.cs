using DubUrl.Schema;
using DubUrl.Schema.Constraints;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class RelationalDdlCapabilityTests
{
    [TestCase(false, "id", ContractConstraintOptions.All, 0)]
    [TestCase(false, "physical_id", ContractConstraintOptions.All, 0)]
    [TestCase(true, "id", ContractConstraintOptions.All, 1)]
    [TestCase(false, "id", ContractConstraintOptions.Unique, 1)]
    public void Redundant_unique_is_removed_only_when_a_matching_single_column_primary_key_is_enabled(
        bool composite, string key, ContractConstraintOptions options, int uniqueCount)
    {
        var dialect = OfflineSqlDialects.Resolve("teradata");
        var contract = Contract(new([new("id", "integer", PhysicalName: "physical_id", Constraints: [new("unique", true)]), new("other", "integer")],
            composite ? [key, "other"] : [key]));
        var plan = new RelationalSchemaPlanner(dialect).PlanSchema(contract, new(options));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(System.Text.RegularExpressions.Regex.Matches(plan.Script, @"\bUNIQUE\b").Count, Is.EqualTo(uniqueCount));
    }

    [TestCase(true, ContractConstraintOptions.All, true)]
    [TestCase(true, ContractConstraintOptions.None, false)]
    [TestCase(false, ContractConstraintOptions.All, false)]
    public void CrateDB_rejects_requested_unique_constraints_before_rendering(bool unique, ContractConstraintOptions options, bool error)
    {
        var dialect = OfflineSqlDialects.Resolve("cratedb");
        var renderer = new Mock<SchemaScriptRenderer>(dialect, SchemaCreationOptions.None, null);
        renderer.Setup(r => r.Render(It.IsAny<Schema>())).Returns("SQL");
        var contract = Contract(new([new("id", "integer", Constraints: [new("unique", unique)])]));
        var plan = new RelationalSchemaPlanner(dialect, renderer.Object).PlanSchema(contract, new(options));
        Assert.That(plan.Diagnostics.Any(d => d.Code == "PROV009" && d.Message.Contains("UNIQUE", StringComparison.Ordinal)), Is.EqualTo(error));
        Assert.That(plan.Script, Is.EqualTo(error ? "" : "SQL"));
        renderer.Verify(r => r.Render(It.IsAny<Schema>()), error ? Times.Never() : Times.Once());
    }

    [Test]
    public void CrateDB_does_not_reject_unique_metadata_already_expressed_by_a_primary_key()
    {
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("cratedb"))
            .PlanSchema(Contract(new([new("id", "integer", Constraints: [new("unique", true)])], ["id"])));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("PRIMARY KEY").And.Not.Contain("UNIQUE"));
    }

    [Test]
    public void Snowflake_reports_unique_metadata_as_information_without_duplicating_single_column_primary_keys()
    {
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("snowflake"))
            .PlanSchema(Contract(new([new("id", "integer", Constraints: [new("unique", true)]),
                new("code", "integer", Constraints: [new("unique", true)])], ["id"])));
        Assert.That(plan.Diagnostics.Select(d => d.Code), Is.EqualTo(new[] { "PROV010", "PROV010" }));
        Assert.That(plan.Diagnostics.Select(d => d.Message), Has.One.Contains("UNIQUE"));
    }

    private static DataContract Contract(DataSchema schema) => new(new("test"), new(),
        [new("members", "members", null, null, AssetKind.Table, schema)], [], new());
}
