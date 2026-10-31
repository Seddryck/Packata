using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Application.Testing;

public class DdlCapabilityTests
{
    private const string Relationship = """
        resources:
          - name: members
            schema:
              fields:
                - { name: country, type: integer }
              foreignKeys:
                - fields: country
                  reference: { resource: countries, fields: id }
          - name: countries
            schema:
              fields:
                - { name: id, type: integer }
              primaryKey: [id]
        """;

    [TestCase("drill")]
    [TestCase("questdb")]
    public async Task Disabled_dialects_are_absent_from_catalog_and_rejected(string dialect)
    {
        var catalog = GenerationCatalogService.Catalog.Targets.Single(t => t.Id == "ddl");
        Assert.That(catalog.Dialects!.Select(d => d.Id), Does.Not.Contain(dialect));
        Assert.Throws<ArgumentException>(() => OfflineSqlDialects.Resolve(dialect));
        var options = new GenerationOptions(Target: "ddl", Dialect: dialect);
        Assert.That(SchemaGenerationService.ValidateOptions(options, null), Is.Not.Null);
        var result = await new SchemaGenerationService().GenerateAsync(Relationship, options);
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("GEN_OPTIONS"));
    }

    [TestCase("cratedb")]
    [TestCase("singlestore")]
    public async Task Unsupported_foreign_keys_fail_without_partial_SQL_and_can_be_explicitly_disabled(string dialect)
    {
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Target: "ddl", Dialect: dialect);
        var result = await service.GenerateAsync(Relationship, options);
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Single(d => d.Code == "PROV009").Severity, Is.EqualTo("Error"));
        Assert.That(result.Diagnostics.Single(d => d.Code == "PROV009").Message, Does.Contain("FOREIGN KEY"));
        var allowed = await service.GenerateAsync(Relationship, options with
        {
            DdlOptions = new(ContractConstraintOptions.All & ~ContractConstraintOptions.ForeignKeys)
        });
        Assert.That(allowed.IsSuccessful, Is.True, string.Join(";", allowed.Diagnostics));
        Assert.That(allowed.Content, Does.Contain("CREATE TABLE").And.Not.Contain("FOREIGN KEY"));
    }

    [Test]
    public async Task SingleStore_rejects_CHECK_constraints_unless_explicitly_disabled()
    {
        var text = Relationship.Replace("type: integer }", "type: integer, constraints: { minimum: 0 } }");
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Target: "ddl", Dialect: "singlestore",
            DdlOptions: new(ContractConstraintOptions.PrimaryKey | ContractConstraintOptions.Checks));
        var result = await service.GenerateAsync(text, options);
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Where(d => d.Code == "PROV009").All(d => d.Message.Contains("CHECK", StringComparison.Ordinal)), Is.True);
        var disabled = await service.GenerateAsync(text, options with { DdlOptions = new(ContractConstraintOptions.PrimaryKey) });
        Assert.That(disabled.IsSuccessful, Is.True, string.Join(";", disabled.Diagnostics));
        Assert.That(disabled.Content, Does.Not.Contain("CHECK"));
    }

    [Test]
    public async Task Unsupported_foreign_keys_are_detected_before_external_contract_reads()
    {
        const string external = """
            {"apiVersion":"v3.2.0","kind":"DataContract","id":"test","schema":[{"name":"members","properties":[{"name":"country","logicalType":"integer","relationships":[{"to":"missing.odcs.yaml#/schema/countries/properties/id"}]}]}]}
            """;
        var result = await new SchemaGenerationService().GenerateAsync(external, new(Target: "ddl", Dialect: "singlestore"));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("PROV009").And.Not.Contain("PROV003"));
    }

    [Test]
    public async Task Snowflake_declares_keys_with_information_about_enforcement()
    {
        var result = await new SchemaGenerationService().GenerateAsync(Relationship, new(Target: "ddl", Dialect: "snowflake"));
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Does.Contain("PRIMARY KEY").And.Contain("FOREIGN KEY"));
        Assert.That(result.Diagnostics.Where(d => d.Code == "PROV010").Count(), Is.EqualTo(2));
        Assert.That(result.Diagnostics.Where(d => d.Code == "PROV010").All(d => d.Severity == "Information"), Is.True);
        var disabled = await new SchemaGenerationService().GenerateAsync(Relationship,
            new(Target: "ddl", Dialect: "snowflake", DdlOptions: new(ContractConstraintOptions.None)));
        Assert.That(disabled.Diagnostics.Where(d => d.Code == "PROV010"), Is.Empty);
    }

    [TestCase("duckdb")]
    [TestCase("sqlite")]
    public async Task Foreign_keys_use_CREATE_TABLE_instead_of_unsupported_ALTER_TABLE(string dialect)
    {
        var result = await new SchemaGenerationService().GenerateAsync(Relationship, new(Target: "ddl", Dialect: dialect));
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Does.Contain("FOREIGN KEY").And.Contain("REFERENCES").And.Not.Contain("ALTER TABLE"));
        if (dialect == "duckdb") Assert.That(result.Content!.IndexOf("CREATE TABLE countries", StringComparison.Ordinal),
            Is.LessThan(result.Content.IndexOf("CREATE TABLE members", StringComparison.Ordinal)));
    }

    [Test]
    public async Task DuckDB_dependency_cycles_fail_without_partial_SQL()
    {
        var text = Relationship.Replace("primaryKey: [id]", "primaryKey: [id]\n      foreignKeys:\n        - fields: id\n          reference: { resource: members, fields: country }");
        var result = await new SchemaGenerationService().GenerateAsync(text, new(Target: "ddl", Dialect: "duckdb"));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Single(d => d.Code == "PROV009").Message, Does.Contain("dependency cycle"));
    }

    [Test]
    public async Task DuckDB_self_references_use_the_upstream_supported_inline_form()
    {
        var text = Relationship.Replace("resource: countries", "resource: members").Replace("fields: id }", "fields: country }")
            .Replace("foreignKeys:", "primaryKey: [country]\n      foreignKeys:");
        var result = await new SchemaGenerationService().GenerateAsync(text, new(Target: "ddl", Dialect: "duckdb"));
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Does.Contain("REFERENCES members (country)").And.Not.Contain("ALTER TABLE"));
    }

    [Test]
    public void DuckDB_external_references_stay_inline_without_creating_the_lookup_table()
    {
        var source = new DataAsset("members", "members", null, null, AssetKind.Table,
            new([new("country", "integer")], Relationships: [new(["country"], "countries", ["id"])]));
        var target = new DataAsset("countries", "countries", null, null, AssetKind.Table, new([new("id", "integer")], ["id"]));
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve("duckdb"))
            .PlanSchema(new(new("test"), new(), [source], [], new()), referencedAssets: [target]);
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("REFERENCES countries").And.Not.Contain("CREATE TABLE countries").And.Not.Contain("ALTER TABLE"));
    }
}
