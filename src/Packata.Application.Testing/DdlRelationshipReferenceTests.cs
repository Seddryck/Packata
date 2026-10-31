using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Provisioning;

namespace Packata.Application.Testing;

public class DdlRelationshipReferenceTests
{
    public const string Members = """
        apiVersion: v3.2.0
        kind: DataContract
        id: membership
        schema:
          - name: members
            physicalName: member_rows
            properties:
              - name: member_id
                logicalType: integer
                primaryKey: true
                required: true
              - name: country
                physicalName: country_code
                logicalType: string
                physicalType: varchar(2)
                relationships:
                  - to: {TARGET}
        """;
    public const string Countries = """
        apiVersion: v3.2.0
        kind: DataContract
        id: lookup
        schema:
          - name: countries
            physicalName: country_lookup
            properties:
              - name: iso_code
                physicalName: ISO2
                logicalType: string
                physicalType: varchar(2)
                primaryKey: true
        servers:
          - server: missing-data
            type: local
            path: never-read-country-records.csv
            format: csv
        """;

    private sealed class Workspace : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "packata-ddl-references-" + Guid.NewGuid(), "contracts with spaces");
        internal Workspace() => Directory.CreateDirectory(Path);
        internal Uri BaseUri => new(Path + System.IO.Path.DirectorySeparatorChar);
        internal void Write(string name, string text) => File.WriteAllText(System.IO.Path.Combine(Path, name), text);
        public void Dispose() => Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true);
    }

    private static GenerationOptions Options => new(Target: "ddl", Dialect: "sqlserver", DdlOptions: new(Descriptions: false));

    [TestCase(false, "sqlserver")]
    [TestCase(true, "sqlserver")]
    [TestCase(false, "sqlite")]
    [TestCase(false, "postgresql")]
    [TestCase(false, "duckdb")]
    public async Task Relative_and_absolute_external_references_validate_names_without_creating_or_reading_target_data(bool absolute, string dialect)
    {
        using var workspace = new Workspace();
        workspace.Write("countries.odcs.yaml", Countries);
        var locator = absolute ? new Uri(System.IO.Path.Combine(workspace.Path, "countries.odcs.yaml")).AbsoluteUri : "countries.odcs.yaml";
        var descriptor = Members.Replace("{TARGET}", locator + "#/schema/countries/properties/iso_code");
        var document = ContractDocument.Parse(descriptor, baseUri: workspace.BaseUri);
        var originalTarget = document.Map().RequireValue().Assets[0].Schema!.Relationships[0].TargetAsset;
        var result = await new SchemaGenerationService().GenerateAsync(document, Options with { Dialect = dialect });
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Diagnostics.Any(d => d.Code is "PROV003" or "PROV004"), Is.False);
        var normalized = result.Content!.Replace("[", "").Replace("]", "").Replace("\"", "");
        Assert.That(normalized, Does.Contain("CREATE TABLE member_rows").And.Match(@"REFERENCES\s+country_lookup\s*\(ISO2\)"));
        Assert.That(normalized, Does.Not.Contain("CREATE TABLE country_lookup").And.Not.Contain("file:///"));
        Assert.That(result.Content, Does.Contain("PRIMARY KEY").And.Contain("FK_members_countries_1"));
        Assert.That(File.Exists(System.IO.Path.Combine(workspace.Path, "never-read-country-records.csv")), Is.False);
        Assert.That(File.ReadAllText(System.IO.Path.Combine(workspace.Path, "countries.odcs.yaml")), Is.EqualTo(Countries));
        Assert.That(document.Map().RequireValue().Assets[0].Schema!.Relationships[0].TargetAsset, Is.EqualTo(originalTarget));
    }

    [TestCase("missing.odcs.yaml#/schema/countries/properties/iso_code", "could not read")]
    [TestCase("countries.odcs.yaml#/schema/absent/properties/iso_code", "does not identify")]
    [TestCase("countries.odcs.yaml#/schema/countries/properties/absent", "does not resolve to a column")]
    [TestCase("https://example.invalid/countries.odcs.yaml#/schema/countries/properties/iso_code", "remote references are not fetched")]
    public async Task Unresolved_external_metadata_produces_an_actionable_error_and_no_partial_SQL(string reference, string reason)
    {
        using var workspace = new Workspace();
        workspace.Write("countries.odcs.yaml", Countries);
        var result = await new SchemaGenerationService().GenerateAsync(Members.Replace("{TARGET}", reference), Options, baseUri: workspace.BaseUri);
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "PROV003" && d.Severity == "Error" && d.Message.Contains(reason)), Is.True, string.Join(";", result.Diagnostics));
    }

    [Test]
    public async Task Invalid_external_contracts_are_diagnosed_without_echoing_their_contents()
    {
        using var workspace = new Workspace();
        workspace.Write("countries.odcs.yaml", "not: [valid\nconfidential-source-marker");
        var result = await new SchemaGenerationService().GenerateAsync(Members.Replace("{TARGET}", "countries.odcs.yaml#/schema/countries/properties/iso_code"), Options, baseUri: workspace.BaseUri);
        Assert.That(result.Diagnostics.Single(d => d.Code == "PROV003").Message, Does.Contain("could not read or map").And.Not.Contain("confidential-source-marker"));
        Assert.That(result.Content, Is.Null);
    }

    [Test]
    public async Task Repeated_references_share_target_metadata_and_preserve_external_endpoint_qualifiers()
    {
        using var workspace = new Workspace();
        const string reference = "countries.odcs.yaml#/schema/countries/properties/iso_code";
        var target = Countries.ReplaceLineEndings("\n").Replace("type: local\n    path: never-read-country-records.csv\n    format: csv",
            "type: sqlserver\n    host: offline.invalid\n    database: reference_db\n    schema: lookups");
        workspace.Write("countries.odcs.yaml", target);
        var descriptor = Members.Replace("{TARGET}", reference + "\n          - to: " + reference);
        var result = await new SchemaGenerationService().GenerateAsync(descriptor, Options, baseUri: workspace.BaseUri);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content!.Split("CREATE TABLE").Length - 1, Is.EqualTo(1));
        Assert.That(result.Content.Split("REFERENCES [reference_db].[lookups].[country_lookup] ([ISO2])").Length - 1, Is.EqualTo(2));
        Assert.That(result.Diagnostics.Any(d => d.Code is "PROV003" or "PROV007"), Is.False);
    }

    [Test]
    public async Task Disabling_foreign_keys_does_not_open_the_referenced_contract()
    {
        using var workspace = new Workspace();
        var options = Options with { DdlOptions = new(ContractConstraintOptions.PrimaryKey | ContractConstraintOptions.Checks, false) };
        var result = await new SchemaGenerationService().GenerateAsync(Members.Replace("{TARGET}", "missing.odcs.yaml#/schema/countries/properties/iso_code"), options, baseUri: workspace.BaseUri);
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Does.Not.Contain("REFERENCES"));
        Assert.That(result.Diagnostics.Any(d => d.Code is "PROV003" or "PROV004"), Is.False);
    }

    [Test]
    public async Task Selecting_one_asset_can_reference_an_unselected_table_in_the_same_contract()
    {
        const string descriptor = """
            resources:
              - name: members
                schema:
                  fields: [{name: country, type: string, x-packata-physicalType: varchar(2)}]
                  foreignKeys:
                    - fields: [country]
                      reference: {resource: countries, fields: [iso_code]}
              - name: countries
                schema:
                  fields: [{name: iso_code, type: string, x-packata-physicalType: varchar(2)}]
                  primaryKey: [iso_code]
            """;
        var result = await new SchemaGenerationService().GenerateAsync(descriptor, Options with { AssetId = "members" });
        Assert.That(result.IsSuccessful, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Content, Does.Contain("CREATE TABLE [members]").And.Contain("REFERENCES [countries] ([iso_code])"));
        Assert.That(result.Content, Does.Not.Contain("CREATE TABLE [countries]"));
    }
}
