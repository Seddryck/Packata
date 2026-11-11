using System.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class ColumnDefaultLiteralTests
{
    [TestCase("string", null, DbType.String, "CURRENT_TIMESTAMP", "'CURRENT_TIMESTAMP'")]
    [TestCase("string", null, DbType.String, "", "''")]
    [TestCase("boolean", null, DbType.Boolean, false, "FALSE")]
    [TestCase("integer", "i16", DbType.Int16, (short)-42, "-42")]
    [TestCase("integer", null, DbType.Int32, 0, "0")]
    [TestCase("integer", "i64", DbType.Int64, 4294967296L, "4294967296")]
    [TestCase("number", "fp32", DbType.Single, 0.125f, "0.125")]
    [TestCase("number", "fp64", DbType.Double, 0.125d, "0.125")]
    public void Generates_typed_defaults(string logical, string? format, DbType type, object value, string expected)
    {
        var plan = Plan("postgresql", new("value", logical, Format: format) { DefaultValue = new(type, value) });
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain($"DEFAULT {expected}").IgnoreCase);
    }

    [Test]
    public void Renders_decimal_and_temporal_literals()
    {
        var fields = new DataField[]
        {
            new("price", "number", PhysicalType: "decimal(8,2)") { DefaultValue = new(DbType.Decimal, 12.50m) },
            new("day", "date") { DefaultValue = new(DbType.Date, new DateOnly(2026, 11, 11)) },
            new("local", "datetime") { DefaultValue = new(DbType.DateTime, new DateTime(2026, 11, 11, 1, 2, 3)) },
            new("offset", "timestamp") { DefaultValue = new(DbType.DateTimeOffset,
                new DateTimeOffset(2026, 11, 11, 1, 2, 3, TimeSpan.FromMinutes(90))) }
        };

        var plan = PlanFields("sqlserver", fields);
        Assert.That(plan.Diagnostics.Where(diagnostic => diagnostic.Code != "PROV005"), Is.Empty);
        Assert.That(plan.Script, Does.Contain("DEFAULT 12.50")
            .And.Contain("'2026-11-11'")
            .And.Contain("'2026-11-11 01:02:03'").And.Contain("'2026-11-11 01:02:03+01:30'"));
    }

    [Test]
    public void Renders_a_time_literal_with_a_time_formatter()
    {
        var plan = Plan("mysql", new("clock", "time") { DefaultValue = new(DbType.Time, new TimeOnly(1, 2, 3)) });
        Assert.That(plan.Diagnostics, Is.Empty);
        Assert.That(plan.Script, Does.Contain("DEFAULT").And.Contain("'01:02:03'"));
    }

    [Test]
    public void Rejects_decimal_values_that_exceed_the_destination_scale()
    {
        var plan = Plan("sqlserver", new DataField("price", "number", PhysicalType: "decimal(8,2)")
        { DefaultValue = new(DbType.Decimal, 12.501m) });
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single(diagnostic => diagnostic.Code == "PROV009").Message,
            Does.Contain("price").And.Contain("scale 2"));
    }

    [Test]
    public void Inserts_an_omitted_value_on_sqlite()
    {
        var plan = Plan("sqlite", new("value", "string") { DefaultValue = new(DbType.String, "O'Brien") });
        Assert.That(plan.Diagnostics, Is.Empty);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = plan.Script;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO sample DEFAULT VALUES";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT value FROM sample";
        Assert.That(command.ExecuteScalar(), Is.EqualTo("O'Brien"));
    }

    [Test]
    public void Rejects_an_incompatible_default_and_omits_an_absent_default()
    {
        var plan = Plan("sqlite", new("value", "string") { DefaultValue = new(DbType.Int32, 0) });
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("field 'value'").And.Contain("Int32"));
        Assert.That(Plan("sqlite", new("value", "string")).Script, Does.Not.Contain("DEFAULT"));
    }

    private static SchemaProvisioningPlan Plan(string dialect, DataField field) => PlanFields(dialect, [field]);

    private static SchemaProvisioningPlan PlanFields(string dialect, DataField[] fields)
    {
        var table = new DataAsset("sample", "sample", null, null, AssetKind.Table, new(fields));
        var contract = new DataContract(new("test"), new(), [table], [], new());
        return new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect)).PlanSchema(contract);
    }
}
