using DuckDB.NET.Data;
using DubUrl.Schema.Renderers;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class ColumnDefaultExpressionTests
{
    [TestCase(ColumnDefaultKind.CurrentTimestamp, "datetime", 0)]
    [TestCase(ColumnDefaultKind.CurrentUtcTimestamp, "datetime", 9)]
    [TestCase(ColumnDefaultKind.CurrentDate, "date", null)]
    [TestCase(ColumnDefaultKind.CurrentTime, "time", null)]
    public void Reports_the_column_when_the_provider_cannot_render_an_expression(
        ColumnDefaultKind kind, string type, int? precision)
    {
        var plan = Plan("ansi", new DataField("sample_value", type) { DefaultExpression = new(kind, precision) });
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("sample_value"));
    }

    [TestCase("string", null)]
    [TestCase("number", "decimal(2,2)")]
    [TestCase("number", "decimal(4,3)", "fp32")]
    public void Rejects_invalid_sequence_destinations(string type, string? physical, string? format = null)
    {
        var field = new DataField("invoice", type, PhysicalType: physical, Format: format)
        {
            DefaultExpression = new(ColumnDefaultKind.NextSequenceValue, Sequence: new("invoices", Schema: "billing"))
        };
        var plan = Plan("duckdb", field);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Where(diagnostic => diagnostic.Code == "PROV009").Select(diagnostic => diagnostic.Message),
            Has.Some.Contains("invoice"));
    }

    [Test]
    public void Sqlite_evaluates_a_utc_timestamp_for_an_omitted_column()
    {
        RequireRenderer("sqlite");
        var plan = Plan("sqlite", new DataField("created_at", "datetime")
        {
            DefaultExpression = new(ColumnDefaultKind.CurrentUtcTimestamp)
        });
        Assert.That(plan.Diagnostics, Is.Empty);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = plan.Script;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO sample DEFAULT VALUES";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT abs(unixepoch(created_at) - unixepoch('now')) <= 5 FROM sample";
        Assert.That(command.ExecuteScalar(), Is.EqualTo(1));
    }

    [Test]
    public void DuckDb_uses_the_external_sequence_and_leaves_it_intact_when_the_table_is_dropped()
    {
        RequireRenderer("duckdb");
        var plan = Plan("duckdb", new DataField("invoice", "integer")
        {
            DefaultExpression = new(ColumnDefaultKind.NextSequenceValue, Sequence: new("Invoice Numbers", Schema: "Billing Schema"))
        });
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("nextval").IgnoreCase);
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA "Billing Schema";
            CREATE SEQUENCE "Billing Schema"."Invoice Numbers" START 10 INCREMENT 3;
            """;
        command.ExecuteNonQuery();
        command.CommandText = plan.Script;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO sample DEFAULT VALUES; INSERT INTO sample DEFAULT VALUES;";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT invoice FROM sample ORDER BY invoice";
        using (var reader = command.ExecuteReader())
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(10));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(13));
        }

        command.CommandText = "DROP TABLE sample";
        command.ExecuteNonQuery();
        command.CommandText = """SELECT nextval('"Billing Schema"."Invoice Numbers"')""";
        Assert.That(command.ExecuteScalar(), Is.EqualTo(16));
    }

    internal static void RequireRenderer(string dialect)
    {
        Assert.That(OfflineSqlDialects.Resolve(dialect).SchemaSqlRenderer, Is.InstanceOf<IColumnDefaultSqlRenderer>(),
            $"The bundled DubUrl {dialect} provider must include a column default renderer.");
    }

    internal static SchemaProvisioningPlan Plan(string dialect, params DataField[] fields)
    {
        var table = new DataAsset("sample", "sample", null, null, AssetKind.Table, new(fields));
        var contract = new DataContract(new("test"), new(), [table], [], new());
        return new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect)).PlanSchema(contract);
    }
}
