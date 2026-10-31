using DubUrl.Schema;
using DubUrl.Schema.Constraints;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class RelationalSchemaValidationTests
{
    [TestCase(ContractConstraintOptions.All, 1)]
    [TestCase(ContractConstraintOptions.Checks, 0)]
    [TestCase(ContractConstraintOptions.PrimaryKey, 1)]
    public void Field_primary_key_metadata_is_not_treated_as_an_unsupported_check(ContractConstraintOptions enabled, int keys)
    {
        var (plan, schema) = Plan(Contract(new([new("member_id", "integer", Constraints: [new("primaryKey", true)])], ["member_id"])), options: new(enabled, false));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(schema.Tables["orders"].Constraints.OfType<PrimaryKeyConstraint>().Count(), Is.EqualTo(keys));
        Assert.That(schema.Tables["orders"].Columns["member_id"].Constraints.OfType<CheckConstraint>(), Is.Empty);
    }

    [Test]
    public void Unrepresented_primary_key_flags_keep_their_unsupported_constraint_diagnostic()
    {
        var (plan, schema) = Plan(Contract(new([new("id", "integer", Constraints: [new("primaryKey", true)])])));
        Assert.That(plan.Diagnostics.Single().Code, Is.EqualTo("PROV004"));
        Assert.That(schema.Tables["orders"].Constraints.OfType<PrimaryKeyConstraint>(), Is.Empty);
    }

    [Test]
    public void Primary_keys_using_physical_column_references_do_not_warn_about_the_logical_field_flag()
    {
        var (plan, schema) = Plan(Contract(new([new("id", "integer", PhysicalName: "physical_id", Constraints: [new("primaryKey", true)])], ["physical_id"])));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(schema.Tables["orders"].Constraints.OfType<PrimaryKeyConstraint>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Reference_metadata_validates_foreign_keys_without_creating_the_referenced_table()
    {
        var source = Contract(new([new("country", "string", PhysicalType: "varchar(2)")], Relationships: [new(["country"], "external-country", ["code"])]));
        var target = new DataAsset("external-country", "countries", "country_lookup", null, AssetKind.Table, new([new("code", "string", PhysicalName: "iso", PhysicalType: "varchar(2)")]));
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("sqlserver"))
            .PlanSchema(source, new(Descriptions: false), referencedAssets: [target]);
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("REFERENCES [country_lookup] ([iso])"));
        Assert.That(plan.Script, Does.Not.Contain("CREATE TABLE [country_lookup]"));
        Assert.That(plan.Script, Does.Contain("FK_orders_external-country_1"));
    }
    private static DataContract Contract(DataSchema schema) => new(new("validation"), new(),
        [new("orders", "orders", null, null, AssetKind.Table, schema)], [], new());

    private static (SchemaProvisioningPlan Plan, Schema Schema) Plan(DataContract contract,
        string dialect = "sqlserver", ContractProvisioningOptions? options = null)
    {
        Schema? captured = null;
        var renderer = new Mock<SchemaScriptRenderer>(OfflineSqlDialects.Resolve(dialect), SchemaCreationOptions.None, null);
        renderer.Setup(r => r.Render(It.IsAny<Schema>())).Callback<Schema>(s => captured = s).Returns("validated");
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect), renderer.Object).PlanSchema(contract, options);
        return (plan, captured!);
    }

    [TestCase("minimum", 1, ">=")]
    [TestCase("maximum", 100, "<=")]
    [TestCase("exclusiveMinimum", 1, ">")]
    [TestCase("exclusiveMaximum", 100, "<")]
    [TestCase("minLength", 2, ">=")]
    [TestCase("maxLength", 20, "<=")]
    public void Comparison_constraints_are_mapped_to_DubUrl_checks(string kind, int value, string op)
    {
        // PostgreSQL TEXT has no native length bound, so maxLength still needs a CHECK.
        var (plan, schema) = Plan(Contract(new([new("code", "string", Constraints: [new(kind, value)])])), "postgresql");
        Assert.That(plan.Diagnostics, Is.Empty);
        var check = schema.Tables["orders"].Columns["code"].Constraints.OfType<CheckConstraint>().Single();
        Assert.That(check.Operator, Is.EqualTo(op));
    }

    public static IEnumerable<object?[]> InvalidEnums => new object?[][]
    {
        [null, "value collection"], ["not-a-collection", "value collection"],
        [Array.Empty<object>(), "no values"], [new object[] { "one", 2 }, "incompatible value types"]
    };

    [TestCaseSource(nameof(InvalidEnums))]
    public void Invalid_enums_report_context_and_omit_the_constraint(object? value, string reason)
    {
        var (plan, schema) = Plan(Contract(new([new("status", "string", Constraints: [new("enum", value)])])));
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain(reason).And.Contain("status"));
        Assert.That(schema.Tables["orders"].Columns["status"].Constraints.OfType<MembershipCheckConstraint>(), Is.Empty);
    }

    [Test]
    public void Null_enum_members_use_DubUrl_null_membership_semantics()
    {
        var (plan, schema) = Plan(Contract(new([new("status", "string", Constraints: [new("enum", new object?[] { "new", null })])])));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(schema.Tables["orders"].Columns["status"].Constraints.OfType<MembershipCheckConstraint>().Single().NullBehavior,
            Is.EqualTo(NullMembershipBehavior.MatchNull));
    }

    [TestCase("missing", "id", "does not resolve")]
    [TestCase("id", "missing", "does not resolve")]
    [TestCase("", "id", "At least one")]
    public void Invalid_foreign_key_columns_are_diagnosed_without_invalid_references(string source, string target, string reason)
    {
        var contract = Contract(new([new("id", "integer")], Relationships:
            [new(source.Length == 0 ? [] : [source], "customers", [target])]));
        contract = contract with { Assets = [.. contract.Assets,
            new("customers", "customers", null, null, AssetKind.Table, new([new("id", "integer")], ["id"]))] };
        var (plan, schema) = Plan(contract);
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain(reason));
        Assert.That(schema.Tables["orders"].Constraints.OfType<ForeignKeyConstraint>(), Is.Empty);
    }

    [Test]
    public void Repeated_primary_key_references_are_not_rendered()
    {
        var (plan, schema) = Plan(Contract(new([new("id", "integer")], ["id", "id"])));
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("duplicate physical columns"));
        Assert.That(schema.Tables["orders"].Constraints.OfType<PrimaryKeyConstraint>(), Is.Empty);
    }

    [Test]
    public void Unsupported_comments_and_native_types_preserve_diagnostics_and_logical_fallback()
    {
        var contract = Contract(new([new("id", "integer", PhysicalType: "unknown_native", Description: "Identifier")]));
        var (plan, schema) = Plan(contract, "ansi");
        Assert.That(plan.Diagnostics.Select(d => d.Code), Is.EquivalentTo(new[] { "PROV005", "PROV012" }));
        Assert.That(schema.Tables["orders"].Columns["id"].Type, Is.EqualTo(System.Data.DbType.Int32));
    }

    [Test]
    public void Dimensionless_native_types_preserve_metadata_and_disabled_constraints()
    {
        var (plan, schema) = Plan(Contract(new([new("name", "string", PhysicalType: "varchar", Required: true,
            Constraints: [new("unique", true), new("minimum", null)])])), options: new(ContractConstraintOptions.None, false));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(schema.Tables["orders"].Columns["name"].NativeType!.Name, Is.EqualTo("varchar"));
        Assert.That(schema.Tables["orders"].Columns["name"].Constraints.OfType<NotNullableConstraint>(), Is.Empty);
    }

    [Test]
    public void Invalid_scope_missing_schema_and_cancellation_do_not_invoke_renderer()
    {
        var renderer = new Mock<SchemaScriptRenderer>(OfflineSqlDialects.Resolve("sqlite"), SchemaCreationOptions.None, null);
        var planner = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("sqlite"), renderer.Object);
        var contract = Contract(new([new("id", "integer")]));
        Assert.Throws<ArgumentException>(() => planner.PlanSchema(contract, assetId: "absent"));
        Assert.Throws<InvalidOperationException>(() => planner.PlanSchema(contract with { Assets = [contract.Assets[0] with { Schema = null }] }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => planner.PlanSchema(contract, cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentException>(() => OfflineSqlDialects.Resolve("unknown"));
        renderer.Verify(r => r.Render(It.IsAny<Schema>()), Times.Never);
    }
}
