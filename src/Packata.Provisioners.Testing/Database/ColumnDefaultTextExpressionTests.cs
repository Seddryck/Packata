using DuckDB.NET.Data;
using Npgsql;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Provisioners.Testing.Database;

public class ColumnDefaultTextExpressionTests
{
    [TestCase("varchar(35)")]
    [TestCase("char(35)")]
    [TestCase("char(37)")]
    public void Rejects_invalid_uuid_widths(string physicalType)
    {
        var field = new DataField("identifier", "string", PhysicalType: physicalType, Constraints: [new("maxLength", 36)])
        { DefaultExpression = new(ColumnDefaultKind.NewUuid) };
        var plan = ColumnDefaultExpressionTests.Plan("sqlserver", field);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Select(diagnostic => diagnostic.Message), Has.Some.Contains("36 characters"));
    }

    [TestCase("varchar(36)", "VARCHAR(36)")]
    [TestCase("char(36)", "CHAR(36)")]
    public void Accepts_uuid_widths_in_supported_text_types(string physicalType, string expectedType)
    {
        ColumnDefaultExpressionTests.RequireRenderer("duckdb");
        var field = new DataField("identifier", "string", PhysicalType: physicalType, Constraints: [new("maxLength", 64)])
        { DefaultExpression = new(ColumnDefaultKind.NewUuid) };
        var plan = ColumnDefaultExpressionTests.Plan("duckdb", field);
        Assert.That(plan.Diagnostics.Where(diagnostic => diagnostic.Code == "PROV009"), Is.Empty);
        Assert.That(plan.Script, Does.Contain(expectedType).And.Contain("DEFAULT"));
    }

    [TestCase(ColumnDefaultKind.CurrentUser)]
    [TestCase(ColumnDefaultKind.SessionUser)]
    public void Uses_the_providers_required_identity_width(ColumnDefaultKind kind)
    {
        ColumnDefaultExpressionTests.RequireRenderer("postgresql");
        var field = new DataField("identity", "string", Constraints: [new("maxLength", 62)])
        { DefaultExpression = new(kind) };
        var plan = ColumnDefaultExpressionTests.Plan("postgresql", field);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("63").And.Contain("identity"));
        var valid = ColumnDefaultExpressionTests.Plan("postgresql", field with { Constraints = [new("maxLength", 63)] });
        Assert.That(valid.Diagnostics, Is.Empty);
        Assert.That(valid.Script, Does.Contain("DEFAULT").And.Contain("(63)"));
    }

    [TestCase("string", null, "positive SQL text length")]
    [TestCase("string", 0, "positive SQL text length")]
    [TestCase("integer", 36, "text column")]
    public void Rejects_invalid_destinations(string type, int? length, string expected)
    {
        var field = new DataField("identifier", type, Constraints: [new("maxLength", length)])
        { DefaultExpression = new(ColumnDefaultKind.NewUuid) };
        var plan = ColumnDefaultExpressionTests.Plan("sqlserver", field);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single(diagnostic => diagnostic.Code == "PROV009").Message,
            Does.Contain("identifier").And.Contain(expected));
    }

    [Test]
    public void Reports_a_provider_without_a_text_default_renderer()
    {
        var field = new DataField("identifier", "string", Constraints: [new("maxLength", 36)])
        { DefaultExpression = new(ColumnDefaultKind.NewUuid) };
        var plan = ColumnDefaultExpressionTests.Plan("ansi", field);
        Assert.That(plan.Script, Is.Empty);
        Assert.That(plan.Diagnostics.Single().Message, Does.Contain("identifier").And.Contain("unsupported"));
    }

    [Test]
    public void PostgreSql_distinguishes_effective_and_session_identity_after_a_role_change()
    {
        ColumnDefaultExpressionTests.RequireRenderer("postgresql");
        var connectionString = Environment.GetEnvironmentVariable("PACKATA_POSTGRES_DEFAULT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Set PACKATA_POSTGRES_DEFAULT_TEST_CONNECTION to run the PostgreSQL role-switch integration test.");

        var plan = ColumnDefaultExpressionTests.Plan("postgresql",
            new DataField("effective_user", "string", Constraints: [new("maxLength", 63)])
                { DefaultExpression = new(ColumnDefaultKind.CurrentUser) },
            new DataField("original_user", "string", Constraints: [new("maxLength", 63)])
                { DefaultExpression = new(ColumnDefaultKind.SessionUser) });
        Assert.That(plan.Diagnostics, Is.Empty);
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SESSION_USER";
        var sessionUser = command.ExecuteScalar();
        var role = "packata_defaults_" + Guid.NewGuid().ToString("N");
        command.CommandText = plan.Script;
        command.ExecuteNonQuery();
        command.CommandText = $"""
            CREATE ROLE {role};
            GRANT INSERT ON sample TO {role};
            SET LOCAL ROLE {role};
            INSERT INTO sample DEFAULT VALUES;
            RESET ROLE;
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT effective_user, original_user FROM sample";
        using var reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(role));
        Assert.That(reader.GetString(1), Is.EqualTo(sessionUser));
    }

    [TestCase(ColumnDefaultKind.CurrentUser, 63)]
    [TestCase(ColumnDefaultKind.SessionUser, 63)]
    [TestCase(ColumnDefaultKind.NewUuid, 36)]
    public void DuckDb_evaluates_identity_and_uuid_for_an_omitted_column(ColumnDefaultKind kind, int length)
    {
        ColumnDefaultExpressionTests.RequireRenderer("duckdb");
        var field = new DataField("generated_value", "string", Constraints: [new("maxLength", length)])
        { DefaultExpression = new(kind) };
        var plan = ColumnDefaultExpressionTests.Plan("duckdb", field);
        Assert.That(plan.Diagnostics, Is.Empty);
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = plan.Script;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO sample DEFAULT VALUES";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT generated_value FROM sample";
        var value = (string)command.ExecuteScalar()!;
        if (kind == ColumnDefaultKind.NewUuid)
            Assert.That(Guid.TryParseExact(value, "D", out _), Is.True);
        else
            Assert.That(value, Is.EqualTo("duckdb"));
    }
}
