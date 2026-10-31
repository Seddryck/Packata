using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class DubUrlDdlIntegrationTests
{
    [TestCase("sqlserver", null, "NVARCHAR(2)")]
    [TestCase("mysql", null, "VARCHAR(2)")]
    [TestCase("teradata", null, "VARCHAR(2)")]
    [TestCase("sqlserver", "nvarchar(1)", "NVARCHAR(1)")]
    public void Native_equal_or_stricter_bounds_replace_only_the_maximum_length_check(string dialect, string? physical, string expected)
    {
        var plan = Plan(dialect, new([new("code", "string", PhysicalType: physical,
            Constraints: [new("maxLength", 2), new("minLength", 1), new("enum", new[] { "A", "BB" })])]));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain(expected).And.Contain(">= 1").And.Contain(" IN (").And.Not.Contain("<= 2"));
    }

    [TestCase("postgresql", null)]
    [TestCase("postgresql", "varchar(2)")]
    [TestCase("sqlserver", "nvarchar(max)")]
    [TestCase("sqlserver", "nvarchar(8)")]
    [TestCase("duckdb", "varchar(2)")]
    [TestCase("sqlite", "varchar(2)")]
    public void Unbounded_wider_or_unenforced_storage_retains_the_maximum_length_check(string dialect, string? physical)
    {
        var plan = Plan(dialect, new([new("code", "string", PhysicalType: physical,
            Constraints: [new("maxLength", 2)])]));
        Assert.That(plan.Diagnostics.All(d => d.Code == "PROV005"), Is.True, string.Join(";", plan.Diagnostics));
        Assert.That(plan.Script, Does.Contain("<= 2"));
    }

    [TestCase(true, true, ContractConstraintOptions.All, false)]
    [TestCase(false, true, ContractConstraintOptions.All, false)]
    [TestCase(true, false, ContractConstraintOptions.All, true)]
    [TestCase(false, false, ContractConstraintOptions.All, true)]
    [TestCase(true, false, ContractConstraintOptions.None, false)]
    public void Native_nullability_is_independent_of_source_property_presence(bool required, bool nullable,
        ContractConstraintOptions options, bool notNull)
    {
        var plan = Plan("sqlserver", new([new("id", "integer", Required: required, Nullable: nullable)]), new(options));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script.Contains("NOT NULL", StringComparison.Ordinal), Is.EqualTo(notNull));
    }

    [TestCase("postgresql")]
    [TestCase("mysql")]
    [TestCase("sqlserver")]
    [TestCase("duckdb")]
    [TestCase("sqlite")]
    [TestCase("snowflake")]
    [TestCase("teradata")]
    public void Upstream_wraps_comparison_and_membership_checks_and_normalizes_unique_keys(string dialect)
    {
        var plan = Plan(dialect, new([new("id", "integer", Constraints: [new("unique", true), new("minimum", 1)]),
            new("category", "string", PhysicalType: "varchar(3)",
                Constraints: [new("enum", new[] { "STD", "PRE", "HON" })])], ["id"]));
        Assert.That(plan.Diagnostics.Any(d => d.Code == "PROV009"), Is.False, string.Join(";", plan.Diagnostics));
        Assert.That(plan.Script, Does.Contain("CHECK (").And.Contain(" IN (").And.Not.Contain("UNIQUE"));
        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(plan.Script, @"CHECK\s+[^\s(]"), Is.False);
    }

    [Test]
    public void SqlServer_uses_MAX_for_unrestricted_text_and_declared_maxLength_for_bounded_strings()
    {
        var plan = Plan("sqlserver", new([new("notes", "string"),
            new("code", "string", Constraints: [new("maxLength", 3)])]), new(ContractConstraintOptions.None));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("[notes] NVARCHAR(MAX)").And.Contain("[code] NVARCHAR(3)"));
    }

    [TestCase("varchar(max)", "VARCHAR(MAX)")]
    [TestCase("nvarchar(MAX)", "NVARCHAR(MAX)")]
    public void Explicit_native_MAX_lengths_are_preserved(string physical, string expected)
    {
        var plan = Plan("sqlserver", new([new("notes", "string", PhysicalType: physical)]));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain(expected));
    }

    [TestCase("mysql", false)]
    [TestCase("mysql", true)]
    [TestCase("sqlserver", false)]
    [TestCase("sqlserver", true)]
    public void Both_foreign_key_endpoints_require_safe_bounded_types_even_for_external_tables(string dialect, bool unsafeTarget)
    {
        var source = new DataAsset("members", "members", null, null, AssetKind.Table,
            new([new("country", "string", PhysicalType: unsafeTarget ? "varchar(2)" : null)],
                Relationships: [new(["country"], "lookup", ["code"])]));
        var target = new DataAsset("lookup", "lookup", null, null, AssetKind.Table,
            new([new("code", "string", PhysicalType: unsafeTarget ? null : "varchar(2)")], ["code"]));
        var contract = new DataContract(new("test"), new(), [source], [], new());
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect)).PlanSchema(contract, referencedAssets: [target]);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single().Code, Is.EqualTo("PROV009"));
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("bounded"));
    }

    [Test]
    public void Teradata_requires_a_declared_string_length_instead_of_guessing()
    {
        var missing = Plan("teradata", new([new("name", "string")]));
        Assert.That(missing.Script, Is.Empty);
        Assert.That(missing.Diagnostics.Single().Message, Does.Contain("explicit bounded length"));
        var bounded = Plan("teradata", new([new("name", "string", Constraints: [new("maxLength", 80)])]));
        Assert.That(bounded.Diagnostics, Is.Empty);
        Assert.That(bounded.Script, Does.Contain("VARCHAR(80)"));
    }

    [TestCase("mysql", "COMMENT 'Owner''s name'")]
    [TestCase("sqlserver", "sp_addextendedproperty")]
    [TestCase("snowflake", "COMMENT ON COLUMN")]
    public void Upstream_provider_comment_renderers_preserve_descriptions(string dialect, string expected)
    {
        var plan = Plan(dialect, new([new("id", "integer", Description: "Owner's name")]));
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain(expected));
    }

    [Test]
    public void Database_comment_limits_are_distinguished_from_missing_renderers()
    {
        var schema = new DataSchema([new("id", "integer", Description: "Identifier")]);
        Assert.That(Plan("sqlite", schema).Diagnostics.Single().Code, Is.EqualTo("PROV012"));
        Assert.That(Plan("teradata", schema).Diagnostics.Single().Code, Is.EqualTo("PROV006"));
    }

    private static SchemaProvisioningPlan Plan(string dialect, DataSchema schema, ContractProvisioningOptions? options = null)
        => new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect)).PlanSchema(
            new(new("test"), new(), [new("members", "members", null, null, AssetKind.Table, schema)], [], new()), options);
}
